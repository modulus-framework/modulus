namespace Modulus.Authorization.Workflow;

/// <summary>Default chain store: in memory, per instance, not tenant aware (development and tests; use the EF store in production).</summary>
public sealed class InMemoryApprovalChainStore : IApprovalChainStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, ApprovalChain> _chains = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<ApprovalChain>> ListAsync(CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<ApprovalChain>>([.. _chains.Values.OrderBy(c => c.Name, StringComparer.Ordinal)]);
    }

    /// <inheritdoc />
    public Task<ApprovalChain?> GetAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult(_chains.GetValueOrDefault(id));
    }

    /// <inheritdoc />
    public Task<ApprovalChain> SaveAsync(ApprovalChain chain, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chain);
        lock (_gate)
            _chains[chain.Id] = chain;
        return Task.FromResult(chain);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult(_chains.Remove(id));
    }
}

/// <summary>Default request store: in memory, per instance, not tenant aware (development and tests; use the EF store in production).</summary>
public sealed class InMemoryApprovalRequestStore : IApprovalRequestStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, ApprovalRequest> _requests = [];

    /// <inheritdoc />
    public Task AddAsync(ApprovalRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
            _requests[request.Id] = request;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ApprovalRequest?> GetAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult(_requests.GetValueOrDefault(id));
    }

    /// <inheritdoc />
    public Task<bool> TryUpdateAsync(ApprovalRequest updated, long expectedVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(updated);
        lock (_gate)
        {
            if (!_requests.TryGetValue(updated.Id, out var current) || current.Version != expectedVersion)
                return Task.FromResult(false);
            _requests[updated.Id] = updated;
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ApprovalRequest>> PendingAsync(CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<ApprovalRequest>>([.. _requests.Values.Where(r => r.Status == ApprovalStatus.Pending).OrderBy(r => r.CreatedAt)]);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ApprovalRequest>> RequestedByAsync(Guid userId, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<ApprovalRequest>>([.. _requests.Values.Where(r => r.RequestedBy == userId).OrderByDescending(r => r.CreatedAt)]);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ApprovalRequest>> ForDocumentAsync(string documentType, string documentId, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<ApprovalRequest>>([.. _requests.Values
                .Where(r => string.Equals(r.DocumentType, documentType, StringComparison.OrdinalIgnoreCase) && string.Equals(r.DocumentId, documentId, StringComparison.Ordinal))
                .OrderByDescending(r => r.CreatedAt)]);
    }
}
