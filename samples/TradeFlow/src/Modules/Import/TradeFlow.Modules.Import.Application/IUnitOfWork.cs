namespace TradeFlow.Modules.Import.Application;

/// <summary>
/// Unit of work for the Import module. Each module defines its own
/// <c>IUnitOfWork</c> registered against its own DbContext so handlers stay
/// persistence-agnostic and there is no DI race between modules.
/// </summary>
public interface IUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}