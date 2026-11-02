namespace Modulus.Tenancy.Lifecycle;

/// <summary>
/// Persistence for lifecycle operations, legal holds and pending deletions. It lives in the host database, not in any company's
/// data, so the record of a deletion survives the deletion. Implementations must be safe for concurrent use by several nodes.
/// </summary>
public interface ITenantLifecycleStore
{
    /// <summary>Adds a new operation.</summary>
    Task AddOperationAsync(LifecycleOperation operation, CancellationToken ct);

    /// <summary>Finds an operation, or returns null.</summary>
    Task<LifecycleOperation?> FindOperationAsync(Guid id, CancellationToken ct);

    /// <summary>Saves the operation's current state.</summary>
    Task SaveOperationAsync(LifecycleOperation operation, CancellationToken ct);

    /// <summary>The operations of one company, newest first.</summary>
    Task<IReadOnlyList<LifecycleOperation>> ListOperationsAsync(Guid tenantId, CancellationToken ct);

    /// <summary>Operations that are pending, running or on hold.</summary>
    Task<IReadOnlyList<LifecycleOperation>> ListActiveOperationsAsync(CancellationToken ct);

    /// <summary>Completed exports whose archive has passed its expiry and still exists.</summary>
    Task<IReadOnlyList<LifecycleOperation>> ListExpiredExportsAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>Adds a legal hold.</summary>
    Task AddLegalHoldAsync(LegalHold hold, CancellationToken ct);

    /// <summary>Finds a legal hold, or returns null.</summary>
    Task<LegalHold?> FindLegalHoldAsync(Guid id, CancellationToken ct);

    /// <summary>Saves a legal hold (for example its release).</summary>
    Task SaveLegalHoldAsync(LegalHold hold, CancellationToken ct);

    /// <summary>The active legal holds of one company.</summary>
    Task<IReadOnlyList<LegalHold>> ListActiveLegalHoldsAsync(Guid tenantId, CancellationToken ct);

    /// <summary>Adds or updates a pending deletion.</summary>
    Task SavePendingDeletionAsync(PendingDeletion item, CancellationToken ct);

    /// <summary>Pending deletions not yet done whose time has come, oldest first.</summary>
    Task<IReadOnlyList<PendingDeletion>> ListDuePendingDeletionsAsync(DateTimeOffset now, int take, CancellationToken ct);
}
