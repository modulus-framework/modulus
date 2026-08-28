namespace Modulus.Core.Abstractions;

/// <summary>
/// Acquires and releases a distributed lock across multiple replicas of the same service.
/// </summary>
public interface IDistributedLock
{
    /// <summary>
    /// Attempts to acquire a lock with the given <paramref name="key"/> for a duration of
    /// <paramref name="duration"/>. Returns a disposable lease that releases the lock when
    /// disposed; returns null if the lock is held by another replica.
    /// </summary>
    /// <remarks>
    /// The lock is automatically released after <paramref name="duration"/> elapses,
    /// even if the lease is never disposed — this prevents deadlocks if a process crashes
    /// mid-operation. The caller should dispose the lease promptly to release the lock
    /// earlier and allow other replicas to proceed.
    /// </remarks>
    Task<IAsyncDisposable?> TryAcquireAsync(
        string key,
        TimeSpan duration,
        CancellationToken ct = default);
}

/// <summary>
/// Marker lease returned by <see cref="IDistributedLock.TryAcquireAsync"/> on success.
/// Disposed to release the lock early; otherwise released automatically after the
/// configured duration expires.
/// </summary>
public interface IDistributedLease : IAsyncDisposable
{
}
