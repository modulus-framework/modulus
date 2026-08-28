using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.WorkflowEngine.Application.Dtos;
using ProcureFlow.Modules.WorkflowEngine.Application.Queries;
using ProcureFlow.Modules.WorkflowEngine.Domain.Entities;
using ProcureFlow.Modules.WorkflowEngine.Domain.Errors;
using ProcureFlow.Modules.WorkflowEngine.Domain.Repositories;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.WorkflowEngine.Application.Queries;

public sealed class GetWorkflowDefinitionByIdHandler(
    IWorkflowDefinitionRepository definitionRepository) : IQueryHandler<GetWorkflowDefinitionByIdQuery, Result<WorkflowDefinitionResponse>>
{
    public async Task<Result<WorkflowDefinitionResponse>> HandleAsync(GetWorkflowDefinitionByIdQuery request, CancellationToken ct)
    {
        WorkflowDefinition? definition = await definitionRepository.GetByIdAsync(request.Id, ct);
        if (definition is null)
            return Result.Failure<WorkflowDefinitionResponse>(WorkflowErrors.DefinitionNotFound(request.Id));

        return Result.Success(WorkflowResponseFactory.ToResponse(definition));
    }
}

public sealed class GetWorkflowDefinitionsByKeyHandler(
    IWorkflowDefinitionRepository definitionRepository,
    ICurrentTenant currentTenant) : IQueryHandler<GetWorkflowDefinitionsByKeyQuery, Result<IReadOnlyList<WorkflowDefinitionResponse>>>
{
    public async Task<Result<IReadOnlyList<WorkflowDefinitionResponse>>> HandleAsync(GetWorkflowDefinitionsByKeyQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<WorkflowDefinition> definitions = await definitionRepository.GetAllByKeyAsync(tenantId, request.Key, ct);
        IReadOnlyList<WorkflowDefinitionResponse> responses = definitions.Select(WorkflowResponseFactory.ToResponse).ToList();
        return Result.Success(responses);
    }
}

public sealed class GetWorkflowInstanceByIdHandler(
    IWorkflowInstanceRepository instanceRepository) : IQueryHandler<GetWorkflowInstanceByIdQuery, Result<WorkflowInstanceResponse>>
{
    public async Task<Result<WorkflowInstanceResponse>> HandleAsync(GetWorkflowInstanceByIdQuery request, CancellationToken ct)
    {
        WorkflowInstance? instance = await instanceRepository.GetByIdAsync(request.Id, ct);
        if (instance is null)
            return Result.Failure<WorkflowInstanceResponse>(WorkflowErrors.InstanceNotFound(request.Id));

        return Result.Success(WorkflowResponseFactory.ToResponse(instance));
    }
}

public sealed class GetMyOpenTasksHandler(
    IWorkflowTaskRepository taskRepository,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : IQueryHandler<GetMyOpenTasksQuery, Result<IReadOnlyList<WorkflowTaskResponse>>>
{
    public async Task<Result<IReadOnlyList<WorkflowTaskResponse>>> HandleAsync(GetMyOpenTasksQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        Guid userId = currentUser.UserId ?? Guid.Empty;
        IReadOnlyList<WorkflowTask> tasks = await taskRepository.GetOpenByAssigneeAsync(tenantId, userId, ct);
        IReadOnlyList<WorkflowTaskResponse> responses = tasks.Select(WorkflowResponseFactory.ToTaskResponse).ToList();
        return Result.Success(responses);
    }
}
