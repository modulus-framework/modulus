namespace Modulus.AuditLogging.EntityFrameworkCore;

using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AuditLogging.Security;
using Modulus.AuditLogging.Security.Retention;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Common;

/// <summary>The EF Core <see cref="IAuditLogStore"/> (business audit log).</summary>
public sealed class EfAuditLogStore(ModulusAuditDbContext db) : IAuditLogStore
{
    /// <inheritdoc />
    public async Task AppendAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        db.AuditLogs.Add(new AuditLogRow
        {
            Id = entry.Id,
            OccurredAt = entry.OccurredAt,
            TenantId = entry.TenantId,
            UserId = entry.UserId,
            UserName = entry.UserName,
            Action = entry.Action,
            Resource = entry.Resource,
            ResourceId = entry.ResourceId,
            Detail = entry.Detail,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PagedList<AuditLogEntry>> QueryAsync(AuditLogQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, InMemoryAuditLogStore.MaxPageSize);

        var rows = db.AuditLogs.AsNoTracking();
        if (query.From is { } from)
            rows = rows.Where(r => r.OccurredAt >= from);
        if (query.To is { } to)
            rows = rows.Where(r => r.OccurredAt <= to);
        if (query.TenantId is { } tenantId)
            rows = rows.Where(r => r.TenantId == tenantId);
        if (query.UserId is { } userId)
            rows = rows.Where(r => r.UserId == userId);
        if (query.Action is { Length: > 0 } action)
            rows = rows.Where(r => r.Action.Contains(action));
        if (query.Resource is { Length: > 0 } resource)
            rows = rows.Where(r => r.Resource != null && r.Resource.Contains(resource));

        var total = await rows.CountAsync(ct).ConfigureAwait(false);
        var items = await rows
            .OrderByDescending(r => r.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedList<AuditLogEntry>
        {
            Items = items.Select(ToEntry).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <inheritdoc />
    public async Task<AuditLogEntry?> GetOrNullAsync(Guid id, CancellationToken ct = default)
        => await db.AuditLogs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false) is { } row
            ? ToEntry(row)
            : null;

    private static AuditLogEntry ToEntry(AuditLogRow row) => new()
    {
        Id = row.Id,
        OccurredAt = row.OccurredAt,
        TenantId = row.TenantId,
        UserId = row.UserId,
        UserName = row.UserName,
        Action = row.Action,
        Resource = row.Resource,
        ResourceId = row.ResourceId,
        Detail = row.Detail,
    };
}

/// <summary>
/// The EF Core <see cref="ISecurityAuditStore"/>. An append reads the chain head, links the event after it and
/// inserts it; the (chain, sequence) key rejects a concurrent writer that read the same head, which then re-reads
/// and retries. A process serializes its own appends, so retries only happen between replicas.
/// </summary>
public sealed class EfSecurityAuditStore(IServiceScopeFactory scopes, TimeProvider clock) : ISecurityAuditStore
{
    /// <summary>Attempts per append before the conflict is reported (default 20).</summary>
    public int MaxConflictRetries { get; init; } = 20;

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public async Task<SecurityAuditRecord> AppendAsync(SecurityAuditEvent auditEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        var chainId = SecurityAuditChain.ChainOf(auditEvent.TenantId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ModulusAuditDbContext>();
                var head = await db.SecurityAuditEntries.AsNoTracking()
                    .Where(r => r.ChainId == chainId)
                    .OrderByDescending(r => r.Sequence)
                    .Select(r => new SecurityAuditHead(r.ChainId, r.Sequence, r.Hash))
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);

                var record = SecurityAuditChain.Link(auditEvent, head, clock.GetUtcNow());
                db.SecurityAuditEntries.Add(ToRow(record));
                try
                {
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    return record;
                }
                catch (DbUpdateException) when (attempt < MaxConflictRetries)
                {
                    // Another replica appended this sequence first: read the new head and link after it.
                    await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5, 25 * attempt)), ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SecurityAuditRecord> ReadAsync(
        Guid chainId, long fromSequence = 1, [EnumeratorCancellation] CancellationToken ct = default)
    {
        const int batch = 500;
        var next = fromSequence;
        while (true)
        {
            List<SecurityAuditRow> rows;
            await using (var scope = scopes.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ModulusAuditDbContext>();
                var from = next;
                rows = await db.SecurityAuditEntries.AsNoTracking()
                    .Where(r => r.ChainId == chainId && r.Sequence >= from)
                    .OrderBy(r => r.Sequence)
                    .Take(batch)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);
            }

            foreach (var row in rows)
                yield return ToRecord(row);

            if (rows.Count < batch)
                yield break;
            next = rows[^1].Sequence + 1;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SecurityAuditRecord>> QueryAsync(SecurityAuditQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ModulusAuditDbContext>();
        var rows = db.SecurityAuditEntries.AsNoTracking().Where(r => r.ChainId == query.ChainId);
        if (query.Category is { } category)
            rows = rows.Where(r => r.Category == category);
        if (query.Actor is { } actor)
            rows = rows.Where(r => r.Actor == actor);
        if (query.ActionPrefix is { } prefix)
            rows = rows.Where(r => r.Action.StartsWith(prefix));
        if (query.Outcome is { } outcome)
            rows = rows.Where(r => r.Outcome == outcome);
        if (query.Since is { } since)
            rows = rows.Where(r => r.OccurredAt >= since);

        var found = await rows.OrderByDescending(r => r.Sequence).Take(query.Limit).ToListAsync(ct).ConfigureAwait(false);
        return [.. found.Select(ToRecord)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SecurityAuditHead>> GetHeadsAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ModulusAuditDbContext>();
        var lasts = db.SecurityAuditEntries
            .GroupBy(r => r.ChainId)
            .Select(g => new { ChainId = g.Key, Sequence = g.Max(r => r.Sequence) });
        return await db.SecurityAuditEntries.AsNoTracking()
            .Join(lasts, r => new { r.ChainId, r.Sequence }, l => new { l.ChainId, l.Sequence }, (r, _) => r)
            .Select(r => new SecurityAuditHead(r.ChainId, r.Sequence, r.Hash))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    internal static SecurityAuditRow ToRow(SecurityAuditRecord record) => new()
    {
        ChainId = record.ChainId,
        Sequence = record.Sequence,
        OccurredAt = record.OccurredAt,
        Category = record.Category,
        Action = record.Action,
        Outcome = record.Outcome,
        Actor = record.Actor,
        Target = record.Target,
        CorrelationId = record.CorrelationId,
        Details = JsonSerializer.Serialize(record.Details),
        PreviousHash = record.PreviousHash,
        Hash = record.Hash,
    };

    internal static SecurityAuditRecord ToRecord(SecurityAuditRow row) => new()
    {
        ChainId = row.ChainId,
        Sequence = row.Sequence,
        OccurredAt = row.OccurredAt,
        Category = row.Category,
        Action = row.Action,
        Outcome = row.Outcome,
        Actor = row.Actor,
        Target = row.Target,
        CorrelationId = row.CorrelationId,
        Details = JsonSerializer.Deserialize<Dictionary<string, string?>>(row.Details) ?? [],
        PreviousHash = row.PreviousHash,
        Hash = row.Hash,
    };

    /// <inheritdoc />
    public async Task<SecurityAuditHead?> GetCheckpointAsync(Guid chainId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ModulusAuditDbContext>();
        return await db.SecurityAuditCheckpoints.AsNoTracking()
            .Where(c => c.ChainId == chainId)
            .Select(c => new SecurityAuditHead(c.ChainId, c.Sequence, c.Hash))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the chain's entries up to the checkpoint and records it. Refused (and nothing deleted) unless the checkpoint is an entry
    /// of the chain and does not move the stored checkpoint backwards. The append-only scripts of the database may refuse the delete;
    /// that refusal reaches the caller unchanged.
    /// </summary>
    public async Task PurgeAsync(SecurityAuditHead checkpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        await using var scope = scopes.CreateAsyncScope();
        // The purge runs as the purge role: the application role has no delete right, and the database trigger allows a delete
        // only for an entry at or below a checkpoint already recorded in the same transaction (see the PostgreSQL script).
        var db = scope.ServiceProvider.GetService<ModulusAuditPurgeDbContext>()
            ?? throw new InvalidOperationException("Retention cannot purge: no purge role is configured (AddModulusAuditPurgeRole).");
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            // A held chain is never purged, whoever asks: the hold is checked here, in the purge's own transaction.
            if (await db.SecurityAuditHolds.AsNoTracking().AnyAsync(h => h.ChainId == checkpoint.ChainId, ct).ConfigureAwait(false))
                throw new InvalidOperationException("The chain is on legal hold: nothing is purged until the hold is released.");

            var anchor = await db.SecurityAuditEntries.AsNoTracking()
                .Where(r => r.ChainId == checkpoint.ChainId && r.Sequence == checkpoint.Sequence)
                .Select(r => r.Hash)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (anchor is null || !string.Equals(anchor, checkpoint.Hash, StringComparison.Ordinal))
                throw new InvalidOperationException("The checkpoint is not an entry of this chain.");

            var stored = await db.SecurityAuditCheckpoints.FirstOrDefaultAsync(c => c.ChainId == checkpoint.ChainId, ct).ConfigureAwait(false);
            if (stored is not null && stored.Sequence > checkpoint.Sequence)
                throw new InvalidOperationException("The checkpoint would move the chain's purge point backwards.");

            // The checkpoint is written first: the database allows the delete only for entries it covers.
            if (stored is null)
            {
                db.SecurityAuditCheckpoints.Add(new SecurityAuditCheckpointRow
                {
                    ChainId = checkpoint.ChainId,
                    Sequence = checkpoint.Sequence,
                    Hash = checkpoint.Hash,
                    PurgedAtUtc = clock.GetUtcNow(),
                });
            }
            else
            {
                stored.Sequence = checkpoint.Sequence;
                stored.Hash = checkpoint.Hash;
                stored.PurgedAtUtc = clock.GetUtcNow();
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await db.SecurityAuditEntries
                .Where(r => r.ChainId == checkpoint.ChainId && r.Sequence <= checkpoint.Sequence)
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
}

/// <summary>
/// The durable legal-hold register in the audit database (A11-1). Placing a hold is the application's to do: it can only protect data, and
/// an existing hold keeps its reason. Releasing a hold deletes the row, which the application user is not granted; the database refuses it,
/// so release is an action of the database owner.
/// </summary>
public sealed class EfSecurityAuditHolds(IServiceScopeFactory scopes, TimeProvider clock) : ISecurityAuditHolds
{
    /// <inheritdoc />
    public async Task<bool> IsHeldAsync(Guid chainId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ModulusAuditDbContext>();
        return await db.SecurityAuditHolds.AsNoTracking().AnyAsync(h => h.ChainId == chainId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PlaceAsync(Guid chainId, string reason, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ModulusAuditDbContext>();
        if (await db.SecurityAuditHolds.AsNoTracking().AnyAsync(h => h.ChainId == chainId, ct).ConfigureAwait(false))
            return;

        db.SecurityAuditHolds.Add(new SecurityAuditHoldRow { ChainId = chainId, Reason = reason, PlacedAtUtc = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ReleaseAsync(Guid chainId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ModulusAuditDbContext>();
        var row = await db.SecurityAuditHolds.FirstOrDefaultAsync(h => h.ChainId == chainId, ct).ConfigureAwait(false);
        if (row is null)
            return false;

        db.SecurityAuditHolds.Remove(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }
}

/// <summary>
/// The security audit archive in the audit database (A11-2). Purged entries are kept one row each, as JSON with their hashes, and are never
/// changed: a second archive of an entry is a no-op, and an archive that disagrees with a stored entry is refused. Each segment verifies on
/// its own, from the head just before it, so a read-back proves the archived entries are the ones that were purged.
/// </summary>
public sealed class EfSecurityAuditArchive(IServiceScopeFactory scopes, TimeProvider clock) : ISecurityAuditArchive
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task ArchiveAsync(Guid chainId, IReadOnlyList<SecurityAuditRecord> records, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
            return;
        if (records.Any(r => r.ChainId != chainId))
            throw new ArgumentException("Every archived entry must belong to the chain being archived.", nameof(records));

        var ordered = records.OrderBy(r => r.Sequence).ToList();
        var first = ordered[0].Sequence;
        var last = ordered[^1].Sequence;

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ModulusAuditDbContext>();

        // The head the segment must follow: the genesis for the chain's first entry, otherwise the archived entry before it. A segment
        // whose predecessor is not archived is checked against its own first link only, so it can still be wrong about that link.
        var before = first == 1
            ? new SecurityAuditHead(chainId, 0, SecurityAuditChain.GenesisHash)
            : await db.SecurityAuditArchives.AsNoTracking()
                .Where(a => a.ChainId == chainId && a.Sequence == first - 1)
                .Select(a => new SecurityAuditHead(chainId, a.Sequence, a.Hash))
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false)
              ?? new SecurityAuditHead(chainId, first - 1, ordered[0].PreviousHash);

        var segment = SecurityAuditChain.VerifyFrom(ordered, before);
        if (!segment.IsValid)
            throw new InvalidOperationException($"The entries to archive do not verify: {segment.Problem}");

        var stored = await db.SecurityAuditArchives.AsNoTracking()
            .Where(a => a.ChainId == chainId && a.Sequence >= first && a.Sequence <= last)
            .ToDictionaryAsync(a => a.Sequence, a => a.Hash, ct)
            .ConfigureAwait(false);

        var missing = new List<SecurityAuditArchiveRow>();
        foreach (var record in ordered)
        {
            if (stored.TryGetValue(record.Sequence, out var hash))
            {
                if (!string.Equals(hash, record.Hash, StringComparison.Ordinal))
                    throw new InvalidOperationException($"The archive already holds a different entry {record.Sequence} for this chain.");
                continue;
            }

            missing.Add(new SecurityAuditArchiveRow
            {
                ChainId = chainId,
                Sequence = record.Sequence,
                PreviousHash = record.PreviousHash,
                Hash = record.Hash,
                Payload = JsonSerializer.Serialize(record, s_json),
                ArchivedAtUtc = clock.GetUtcNow(),
            });
        }

        if (missing.Count == 0)
            return;
        db.SecurityAuditArchives.AddRange(missing);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The archived entries of one chain, in sequence order.</summary>
    public async Task<IReadOnlyList<SecurityAuditRecord>> ReadAsync(Guid chainId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ModulusAuditDbContext>();
        var rows = await db.SecurityAuditArchives.AsNoTracking()
            .Where(a => a.ChainId == chainId)
            .OrderBy(a => a.Sequence)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.Select(r => JsonSerializer.Deserialize<SecurityAuditRecord>(r.Payload, s_json)
            ?? throw new InvalidOperationException($"Archived entry {r.Sequence} of the chain is not readable.")).ToList();
    }

    /// <summary>
    /// Verifies the archived entries of one chain from the start of the chain: each entry's hash, its link to the one before, and the
    /// sequence. Archived segments are contiguous from the first entry, so one pass covers them all.
    /// </summary>
    public async Task<SecurityAuditVerification> VerifyAsync(Guid chainId, CancellationToken ct = default)
    {
        var records = await ReadAsync(chainId, ct).ConfigureAwait(false);
        if (records.Count == 0)
            return new SecurityAuditVerification(true, 0, null);
        return SecurityAuditChain.VerifyFrom(records, new SecurityAuditHead(chainId, records[0].Sequence - 1, records[0].PreviousHash));
    }
}
