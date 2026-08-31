namespace TradeFlow.Modules.SpendAnalysis.Application;

/// <summary>
/// Unit of work for the SpendAnalysis module.
/// </summary>
public interface IUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
