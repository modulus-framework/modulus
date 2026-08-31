namespace TradeFlow.Modules.OrgStructure.Application.Abstractions;

public interface IUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
