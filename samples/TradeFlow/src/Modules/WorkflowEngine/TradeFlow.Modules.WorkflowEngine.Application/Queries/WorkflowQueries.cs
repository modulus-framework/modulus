using TradeFlow.Modules.WorkflowEngine.Application.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.WorkflowEngine.Application.Queries;

public sealed record GetWorkflowDefinitionByIdQuery(Guid Id) : Modulus.Mediator.Abstractions.IQuery<Result<WorkflowDefinitionResponse>>;

public sealed record GetWorkflowDefinitionsByKeyQuery(string Key) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<WorkflowDefinitionResponse>>>;

public sealed record GetWorkflowInstanceByIdQuery(Guid Id) : Modulus.Mediator.Abstractions.IQuery<Result<WorkflowInstanceResponse>>;

public sealed record GetMyOpenTasksQuery() : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<WorkflowTaskResponse>>>;
