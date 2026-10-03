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
