namespace ModulusSample.Modules.Identity.Application.Abstractions.Data;

public interface IUnitOfWork
{
    /// <summary>
    /// Saves all changes made in this unit of work
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of entities affected</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// Detaches all tracked entities from the change tracker.
    ///
    /// Call this before each retry attempt in a concurrency-retry loop so that:
    ///   1. EF does not return stale cached instances from the identity map.
    ///   2. EF does not preserve stale "original values" for concurrency tokens
    ///      when merging a fresh DB row into an already-tracked entry.
    ///
    /// Without this, retrying inside the same DbContext scope will always send
    /// the original (pre-conflict) Version value in the WHERE clause → 0 rows
    /// affected → DbUpdateConcurrencyException on every retry, not just the first.
    /// </summary>
    void ClearChangeTracker();
}
