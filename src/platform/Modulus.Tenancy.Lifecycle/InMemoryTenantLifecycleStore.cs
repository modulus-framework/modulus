namespace Modulus.Tenancy.Lifecycle;

/// <summary>
/// A store in process memory: for tests and development. Its history is lost on restart, so production uses the Entity
/// Framework Core store. The default registration, <c>AddTenantLifecycle</c>, uses this one unless another store is registered.
/// </summary>
public sealed class InMemoryTenantLifecycleStore : ITenantLifecycleStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, LifecycleOperation> _operations = [];
    private readonly Dictionary<Guid, LegalHold> _holds = [];
    private readonly Dictionary<Guid, PendingDeletion> _pending = [];

    /// <inheritdoc />
    public Task AddOperationAsync(LifecycleOperation operation, CancellationToken ct)
    {
        lock (_gate)
            _operations[operation.Id] = operation;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<LifecycleOperation?> FindOperationAsync(Guid id, CancellationToken ct)
    {
        lock (_gate)
            return Task.FromResult(_operations.GetValueOrDefault(id));
    }

    /// <inheritdoc />
    public Task SaveOperationAsync(LifecycleOperation operation, CancellationToken ct)
    {
        lock (_gate)
            _operations[operation.Id] = operation;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LifecycleOperation>> ListOperationsAsync(Guid tenantId, CancellationToken ct)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<LifecycleOperation>>(
                _operations.Values.Where(o => o.TenantId == tenantId).OrderByDescending(o => o.RequestedAt).ToList());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LifecycleOperation>> ListActiveOperationsAsync(CancellationToken ct)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<LifecycleOperation>>(_operations.Values
                .Where(o => o.State is OperationState.Pending or OperationState.Running or OperationState.OnHold)
                .OrderBy(o => o.RequestedAt).ToList());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LifecycleOperation>> ListExpiredExportsAsync(DateTimeOffset now, CancellationToken ct)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<LifecycleOperation>>(_operations.Values
                .Where(o => o.Kind == OperationKind.Export || o.Kind == OperationKind.Offboarding)
                .Where(o => o.ExportPath is not null && o.ExportExpiresAt <= now)
                .ToList());
    }

    /// <inheritdoc />
    public Task AddLegalHoldAsync(LegalHold hold, CancellationToken ct)
    {
        lock (_gate)
            _holds[hold.Id] = hold;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<LegalHold?> FindLegalHoldAsync(Guid id, CancellationToken ct)
    {
        lock (_gate)
            return Task.FromResult(_holds.GetValueOrDefault(id));
    }

    /// <inheritdoc />
    public Task SaveLegalHoldAsync(LegalHold hold, CancellationToken ct)
    {
        lock (_gate)
            _holds[hold.Id] = hold;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LegalHold>> ListActiveLegalHoldsAsync(Guid tenantId, CancellationToken ct)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<LegalHold>>(
                _holds.Values.Where(h => h.TenantId == tenantId && h.IsActive).ToList());
    }

    /// <inheritdoc />
    public Task SavePendingDeletionAsync(PendingDeletion item, CancellationToken ct)
    {
        lock (_gate)
            _pending[item.Id] = item;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PendingDeletion>> ListDuePendingDeletionsAsync(DateTimeOffset now, int take, CancellationToken ct)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<PendingDeletion>>(_pending.Values
                .Where(p => p.DoneAt is null && p.DeleteAfter <= now)
                .OrderBy(p => p.DeleteAfter)
                .Take(take)
                .ToList());
    }
}
