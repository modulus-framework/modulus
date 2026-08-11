namespace ProcureFlow.Modules.Finance.Application;

/// <summary>
/// Unit of work for the Finance module. Each module defines its own
/// <c>IUnitOfWork</c> and registers it against its own DbContext so the
/// Application layer stays persistence-agnostic and there is no DI race between
/// modules.
/// </summary>
public interface IUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
