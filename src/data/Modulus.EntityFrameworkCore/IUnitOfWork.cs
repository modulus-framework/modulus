namespace Modulus.EntityFrameworkCore.Abstractions;

/// <summary>
/// Commits all pending changes in the current scope.
/// Implemented by ModuleDbContext.
/// Triggers audit fields, domain event dispatch, and outbox writes.
/// </summary>
public interface IUnitOfWork
{
    Task<int> CommitAsync(CancellationToken ct = default);
}
