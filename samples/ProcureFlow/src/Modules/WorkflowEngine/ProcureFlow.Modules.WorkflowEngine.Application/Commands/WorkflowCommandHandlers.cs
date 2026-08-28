using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.WorkflowEngine.Application.Commands;
using ProcureFlow.Modules.WorkflowEngine.Application.Dtos;
using ProcureFlow.Modules.WorkflowEngine.Domain.Entities;
using ProcureFlow.Modules.WorkflowEngine.Domain.Enums;
using ProcureFlow.Modules.WorkflowEngine.Domain.Errors;
using ProcureFlow.Modules.WorkflowEngine.Domain.Repositories;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.WorkflowEngine.Application.Commands;

public sealed class CreateWorkflowDefinitionHandler(
    IWorkflowDefinitionRepository definitionRepository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateWorkflowDefinitionCommand, Result<WorkflowDefinitionResponse>>
{
    public async Task<Result<WorkflowDefinitionResponse>> HandleAsync(CreateWorkflowDefinitionCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;

        var definition = WorkflowDefinition.CreateDraft(
            tenantId, request.Key, request.Name, request.DocumentType,
            request.TriggerEvent, request.StepsJson, request.ContextSchemaJson,
            request.OnRejectJson, request.OnTimeoutAction);

        await definitionRepository.AddAsync(definition, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(WorkflowResponseFactory.ToResponse(definition));
    }
}

public sealed class PublishWorkflowDefinitionHandler(
    IWorkflowDefinitionRepository definitionRepository,
    IUnitOfWork unitOfWork) : ICommandHandler<PublishWorkflowDefinitionCommand, Result<WorkflowDefinitionResponse>>
{
    public async Task<Result<WorkflowDefinitionResponse>> HandleAsync(PublishWorkflowDefinitionCommand request, CancellationToken ct)
    {
        WorkflowDefinition? definition = await definitionRepository.GetByIdAsync(request.DefinitionId, ct);
        if (definition is null)
            return Result.Failure<WorkflowDefinitionResponse>(WorkflowErrors.DefinitionNotFound(request.DefinitionId));

        Result publish = definition.Publish(request.PublishedBy);
        if (publish.IsFailure)
            return Result.Failure<WorkflowDefinitionResponse>(publish.Error);

        await definitionRepository.SaveAsync(definition, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(WorkflowResponseFactory.ToResponse(definition));
    }
}

public sealed class StartWorkflowInstanceHandler(
    IWorkflowDefinitionRepository definitionRepository,
    IWorkflowInstanceRepository instanceRepository,
    IWorkflowTaskRepository taskRepository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<StartWorkflowInstanceCommand, Result<WorkflowInstanceResponse>>
{
    public async Task<Result<WorkflowInstanceResponse>> HandleAsync(StartWorkflowInstanceCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;

        WorkflowDefinition? definition = await definitionRepository.GetPublishedByKeyAsync(tenantId, request.DefinitionKey, ct);
        if (definition is null)
            return Result.Failure<WorkflowInstanceResponse>(WorkflowErrors.DefinitionNotPublished(request.DefinitionKey));

        var instance = WorkflowInstance.Start(
            tenantId, definition.Id, definition.Key, definition.Version,
            request.DocumentType, request.DocumentId, request.ContextJson);

        instance.RecordEvent("InstanceStarted", null, currentUser.UserName);

        // Parse steps and create tasks for the first approval step(s)
        // Simplified: create one Open task per definition step that is an approval type
        var steps = ParseApprovalSteps(definition.StepsJson);
        foreach (var step in steps)
        {
            var task = new WorkflowTask(
                instance.Id, instance.Id, step.StepId, step.StepType,
                null, step.DefaultRole, null,
                step.SlaHours.HasValue ? DateTime.UtcNow.AddHours(step.SlaHours.Value) : null);
            instance.AddTask(task);
        }

        await instanceRepository.AddAsync(instance, ct);
        foreach (WorkflowTask task in instance.Tasks)
        {
            await taskRepository.AddAsync(task, ct);
        }
        await unitOfWork.CommitAsync(ct);
        return Result.Success(WorkflowResponseFactory.ToResponse(instance));
    }

    private static List<ParsedStep> ParseApprovalSteps(string stepsJson)
    {
        var steps = new List<ParsedStep>();
        // Minimal JSON parse — in production use System.Text.Json deserialization
        // For now, scan for "type":"approval" or "type":"approval-chain"
        if (stepsJson.Contains("approval", StringComparison.OrdinalIgnoreCase))
        {
            steps.Add(new ParsedStep("step-1", "approval", null, 24));
        }
        return steps;
    }

    private sealed record ParsedStep(string StepId, string StepType, string? DefaultRole, int? SlaHours);
}

public sealed class CompleteTaskHandler(
    IWorkflowTaskRepository taskRepository,
    IWorkflowInstanceRepository instanceRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<CompleteTaskCommand, Result<WorkflowInstanceResponse>>
{
    public async Task<Result<WorkflowInstanceResponse>> HandleAsync(CompleteTaskCommand request, CancellationToken ct)
    {
        WorkflowTask? task = await taskRepository.GetByIdAsync(request.TaskId, ct);
        if (task is null)
            return Result.Failure<WorkflowInstanceResponse>(WorkflowErrors.TaskNotFound(request.TaskId));

        var decision = (TaskDecision)request.Decision;
        Guid userId = currentUser.UserId ?? Guid.Empty;

        Result complete = task.Complete(decision, userId, request.Reason);
        if (complete.IsFailure)
            return Result.Failure<WorkflowInstanceResponse>(complete.Error);

        WorkflowInstance? instance = await instanceRepository.GetByIdAsync(task.InstanceId, ct);
        if (instance is null)
            return Result.Failure<WorkflowInstanceResponse>(WorkflowErrors.InstanceNotFound(task.InstanceId));

        instance.RecordEvent("TaskCompleted", $"{{\"taskId\":\"{task.Id}\",\"decision\":\"{decision}\"}}", currentUser.UserName);

        // If all tasks are done, complete the instance
        bool allDone = instance.Tasks.All(t => t.Status == Domain.Enums.TaskStatus.Done);
        if (allDone)
        {
            instance.Complete(currentUser.UserName ?? "system");
        }

        await taskRepository.SaveAsync(task, ct);
        await instanceRepository.SaveAsync(instance, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(WorkflowResponseFactory.ToResponse(instance));
    }
}

public sealed class ReassignTaskHandler(
    IWorkflowTaskRepository taskRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<ReassignTaskCommand, Result<WorkflowTaskResponse>>
{
    public async Task<Result<WorkflowTaskResponse>> HandleAsync(ReassignTaskCommand request, CancellationToken ct)
    {
        WorkflowTask? task = await taskRepository.GetByIdAsync(request.TaskId, ct);
        if (task is null)
            return Result.Failure<WorkflowTaskResponse>(WorkflowErrors.TaskNotFound(request.TaskId));

        Result reassign = task.Reassign(request.NewAssigneeUserId);
        if (reassign.IsFailure)
            return Result.Failure<WorkflowTaskResponse>(reassign.Error);

        await taskRepository.SaveAsync(task, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(WorkflowResponseFactory.ToTaskResponse(task));
    }
}
