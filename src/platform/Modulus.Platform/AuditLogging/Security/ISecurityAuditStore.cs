namespace Modulus.AuditLogging.Security;

using System.Runtime.CompilerServices;
using Modulus.Core.Abstractions;

/// <summary>
/// Where the security audit chains live. <see cref="AppendAsync"/> must keep each chain contiguous under
/// concurrency (a durable store uses a unique (chain, sequence) key and retries on conflict).
/// </summary>
public interface ISecurityAuditStore
{
    /// <summary>Sequences, hashes and stores the event at the end of its tenant's chain.</summary>
    Task<SecurityAuditRecord> AppendAsync(SecurityAuditEvent auditEvent, CancellationToken ct = default);

    /// <summary>The entries of one chain in sequence order, starting at <paramref name="fromSequence"/>.</summary>
    IAsyncEnumerable<SecurityAuditRecord> ReadAsync(Guid chainId, long fromSequence = 1, CancellationToken ct = default);

    /// <summary>The last entry of every chain.</summary>
    Task<IReadOnlyList<SecurityAuditHead>> GetHeadsAsync(CancellationToken ct = default);

    /// <summary>
    /// The entries of one chain that match <paramref name="query"/>, newest first. The default reads the chain and filters in memory
    /// (correct for any store, slow for a long chain); a database store overrides it to filter in the database.
    /// </summary>
    async Task<IReadOnlyList<SecurityAuditRecord>> QueryAsync(SecurityAuditQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var matches = new List<SecurityAuditRecord>();
        await foreach (var record in ReadAsync(query.ChainId, 1, ct).ConfigureAwait(false))
        {
            if (query.Matches(record))
                matches.Add(record);
        }

        matches.Reverse();
        return [.. matches.Take(query.Limit)];
    }
}

/// <summary>A filter over one security audit chain; every set field must match.</summary>
/// <param name="ChainId">The chain: the company id, or <see cref="Guid.Empty"/> for the host.</param>
/// <param name="Category">Only this category (<see cref="Modulus.Core.Abstractions.SecurityAuditCategories"/>).</param>
/// <param name="Actor">Only entries by this actor (a user id).</param>
/// <param name="ActionPrefix">Only actions that start with this text (<c>signin.</c>, <c>token.</c>).</param>
/// <param name="Outcome">Only this outcome.</param>
/// <param name="Since">Only entries at or after this time.</param>
/// <param name="Take">The most entries returned (1 to 1000, default 100).</param>
public sealed record SecurityAuditQuery(
    Guid ChainId, string? Category = null, string? Actor = null, string? ActionPrefix = null, string? Outcome = null,
    DateTimeOffset? Since = null, int Take = 100)
{
    /// <summary><see cref="Take"/> held to 1..1000.</summary>
    public int Limit => Math.Clamp(Take, 1, 1000);

    /// <summary>Whether <paramref name="record"/> passes every filter.</summary>
    public bool Matches(SecurityAuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return (Category is null || string.Equals(record.Category, Category, StringComparison.Ordinal))
               && (Actor is null || string.Equals(record.Actor, Actor, StringComparison.Ordinal))
               && (ActionPrefix is null || record.Action.StartsWith(ActionPrefix, StringComparison.Ordinal))
               && (Outcome is null || string.Equals(record.Outcome, Outcome, StringComparison.Ordinal))
               && (Since is null || record.OccurredAt >= Since);
    }
}

/// <summary>Chain verification over any <see cref="ISecurityAuditStore"/>.</summary>
public static class SecurityAuditStoreExtensions
{
    /// <summary>
    /// Re-reads a tenant's chain (null = host) and checks it with <see cref="SecurityAuditChain.Verify"/>. Pass the
    /// last anchored head to also detect a chain that was deleted or recomputed after an edit.
    /// </summary>
    public static async Task<SecurityAuditVerification> VerifyChainAsync(
        this ISecurityAuditStore store, Guid? tenantId, SecurityAuditHead? anchor = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var records = new List<SecurityAuditRecord>();
        await foreach (var record in store.ReadAsync(SecurityAuditChain.ChainOf(tenantId), 1, ct).ConfigureAwait(false))
            records.Add(record);
        return SecurityAuditChain.Verify(records, anchor);
    }
}

/// <summary>
/// Single-node, non-durable <see cref="ISecurityAuditStore"/> (the default). Use the EF Core store
/// (<c>Modulus.AuditLogging.EntityFrameworkCore</c>) in production.
/// </summary>
public sealed class InMemorySecurityAuditStore(TimeProvider clock) : ISecurityAuditStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, List<SecurityAuditRecord>> _chains = [];

    /// <inheritdoc />
    public Task<SecurityAuditRecord> AppendAsync(SecurityAuditEvent auditEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        lock (_gate)
        {
            var chainId = SecurityAuditChain.ChainOf(auditEvent.TenantId);
            if (!_chains.TryGetValue(chainId, out var chain))
                _chains[chainId] = chain = [];
            var last = chain.Count == 0 ? null : chain[^1];
            var record = SecurityAuditChain.Link(
                auditEvent, last is null ? null : new SecurityAuditHead(chainId, last.Sequence, last.Hash), clock.GetUtcNow());
            chain.Add(record);
            return Task.FromResult(record);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SecurityAuditRecord> ReadAsync(
        Guid chainId, long fromSequence = 1, [EnumeratorCancellation] CancellationToken ct = default)
    {
        List<SecurityAuditRecord> snapshot;
        lock (_gate)
            snapshot = _chains.TryGetValue(chainId, out var chain) ? chain.Where(r => r.Sequence >= fromSequence).ToList() : [];

        foreach (var record in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            yield return record;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SecurityAuditHead>> GetHeadsAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyList<SecurityAuditHead> heads = _chains
                .Where(c => c.Value.Count > 0)
                .Select(c => new SecurityAuditHead(c.Key, c.Value[^1].Sequence, c.Value[^1].Hash))
                .ToList();
            return Task.FromResult(heads);
        }
    }
}
