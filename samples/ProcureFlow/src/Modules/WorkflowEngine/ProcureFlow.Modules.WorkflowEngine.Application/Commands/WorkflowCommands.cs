using ProcureFlow.Modules.WorkflowEngine.Application.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.WorkflowEngine.Application.Commands;

public sealed record CreateWorkflowDefinitionCommand(
    string Key,
    string Name,
    string DocumentType,
    string TriggerEvent,
    string StepsJson,
    string? ContextSchemaJson,
    string? OnRejectJson,
    string? OnTimeoutAction) : Modulus.Mediator.Abstractions.ICommand<Result<WorkflowDefinitionResponse>>;

public sealed record PublishWorkflowDefinitionCommand(
    Guid DefinitionId,
    string PublishedBy) : Modulus.Mediator.Abstractions.ICommand<Result<WorkflowDefinitionResponse>>;

public sealed record StartWorkflowInstanceCommand(
    string DefinitionKey,
    string DocumentType,
    Guid DocumentId,
    string? ContextJson) : Modulus.Mediator.Abstractions.ICommand<Result<WorkflowInstanceResponse>>;

public sealed record CompleteTaskCommand(
    Guid TaskId,
    int Decision,
    string? Reason) : Modulus.Mediator.Abstractions.ICommand<Result<WorkflowInstanceResponse>>;

public sealed record ReassignTaskCommand(
    Guid TaskId,
    Guid NewAssigneeUserId) : Modulus.Mediator.Abstractions.ICommand<Result<WorkflowTaskResponse>>;
