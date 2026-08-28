using ProcureFlow.Modules.WorkflowEngine.Application.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.WorkflowEngine.Application.Queries;

public sealed record GetWorkflowDefinitionByIdQuery(Guid Id) : Modulus.Mediator.Abstractions.IQuery<Result<WorkflowDefinitionResponse>>;

public sealed record GetWorkflowDefinitionsByKeyQuery(string Key) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<WorkflowDefinitionResponse>>>;

public sealed record GetWorkflowInstanceByIdQuery(Guid Id) : Modulus.Mediator.Abstractions.IQuery<Result<WorkflowInstanceResponse>>;

public sealed record GetMyOpenTasksQuery() : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<WorkflowTaskResponse>>>;
