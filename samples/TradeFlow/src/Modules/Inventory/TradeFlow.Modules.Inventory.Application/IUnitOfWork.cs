namespace TradeFlow.Modules.Inventory.Application;

/// <summary>
/// Unit of work for the Inventory module. Each module defines its own
/// <c>IUnitOfWork</c> registered against its own DbContext so handlers stay
/// persistence-agnostic and there is no DI race between modules.
/// </summary>
public interface IUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}