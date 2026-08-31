using TradeFlow.Modules.WorkflowEngine.Domain.Entities;
using TradeFlow.Modules.WorkflowEngine.Domain.Enums;

namespace TradeFlow.Modules.WorkflowEngine.Application.Dtos;

public sealed record WorkflowDefinitionResponse(
    Guid Id,
    string Key,
    string Name,
    int Version,
    string DocumentType,
    string TriggerEvent,
    string StepsJson,
    string? ContextSchemaJson,
    string? OnRejectJson,
    string? OnTimeoutAction,
    DefinitionStatus Status,
    string? PublishedBy,
    DateTime? PublishedAtUtc,
    DateTime CreatedAtUtc);

public sealed record WorkflowInstanceResponse(
    Guid Id,
    Guid DefinitionId,
    string DefinitionKey,
    int DefinitionVersion,
    string DocumentType,
    Guid DocumentId,
    string? ContextJson,
    InstanceStatus Status,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? CompletedBy,
    IReadOnlyList<WorkflowTaskResponse> Tasks);

public sealed record WorkflowTaskResponse(
    Guid Id,
    Guid InstanceId,
    string StepId,
    string StepType,
    Guid? AssigneeUserId,
    string? AssigneeRole,
    Domain.Enums.TaskStatus Status,
    TaskDecision? Decision,
    string? Reason,
    Guid? ActedByUserId,
    DateTime? ActedAtUtc,
    DateTime? DueAtUtc,
    DateTime CreatedAtUtc);

public static class WorkflowResponseFactory
{
    public static WorkflowDefinitionResponse ToResponse(WorkflowDefinition d) => new(
        d.Id, d.Key, d.Name, d.Version, d.DocumentType, d.TriggerEvent,
        d.StepsJson, d.ContextSchemaJson, d.OnRejectJson, d.OnTimeoutAction,
        d.Status, d.PublishedBy, d.PublishedAtUtc, d.CreatedAtUtc);

    public static WorkflowInstanceResponse ToResponse(WorkflowInstance i) => new(
        i.Id, i.DefinitionId, i.DefinitionKey, i.DefinitionVersion,
        i.DocumentType, i.DocumentId, i.ContextJson, i.Status,
        i.StartedAtUtc, i.CompletedAtUtc, i.CompletedBy,
        i.Tasks.Select(ToTaskResponse).ToList());

    public static WorkflowTaskResponse ToTaskResponse(WorkflowTask t) => new(
        t.Id, t.InstanceId, t.StepId, t.StepType,
        t.AssigneeUserId, t.AssigneeRole, t.Status,
        t.Decision, t.Reason, t.ActedByUserId,
        t.ActedAtUtc, t.DueAtUtc, t.CreatedAtUtc);
}
