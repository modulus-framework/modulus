namespace TradeFlow.Modules.WorkflowEngine.Application;

/// <summary>
/// Unit of work for the Workflow Engine module.
/// </summary>
public interface IUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
