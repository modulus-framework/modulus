using ProcureFlow.Modules.WorkflowEngine.Domain.Entities;

namespace ProcureFlow.Modules.WorkflowEngine.Domain.Repositories;

public interface IWorkflowDefinitionRepository
{
    Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<WorkflowDefinition?> GetLatestByKeyAsync(Guid tenantId, string key, CancellationToken ct = default);
    Task<WorkflowDefinition?> GetPublishedByKeyAsync(Guid tenantId, string key, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowDefinition>> GetAllByKeyAsync(Guid tenantId, string key, CancellationToken ct = default);
    Task AddAsync(WorkflowDefinition definition, CancellationToken ct = default);
    Task SaveAsync(WorkflowDefinition definition, CancellationToken ct = default);
    Task<bool> ExistsByKeyAndVersionAsync(Guid tenantId, string key, int version, CancellationToken ct = default);
}

public interface IWorkflowInstanceRepository
{
    Task<WorkflowInstance?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowInstance>> GetByDocumentAsync(string documentType, Guid documentId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowInstance>> GetByAssigneeAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
    Task AddAsync(WorkflowInstance instance, CancellationToken ct = default);
    Task SaveAsync(WorkflowInstance instance, CancellationToken ct = default);
}

public interface IWorkflowTaskRepository
{
    Task<WorkflowTask?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowTask>> GetByInstanceIdAsync(Guid instanceId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowTask>> GetOpenByAssigneeAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
    Task AddAsync(WorkflowTask task, CancellationToken ct = default);
    Task SaveAsync(WorkflowTask task, CancellationToken ct = default);
}
