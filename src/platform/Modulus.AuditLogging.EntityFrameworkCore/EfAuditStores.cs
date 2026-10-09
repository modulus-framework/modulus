namespace Modulus.AuditLogging.EntityFrameworkCore;

using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AuditLogging.Security;
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
}
