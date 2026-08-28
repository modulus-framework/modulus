using ProcureFlow.Modules.WorkflowEngine.Domain.Entities;
using ProcureFlow.Modules.WorkflowEngine.Domain.Repositories;
using ProcureFlow.Modules.WorkflowEngine.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace ProcureFlow.Modules.WorkflowEngine.Infrastructure.Repositories;

internal sealed class EfWorkflowDefinitionRepository(WorkflowDbContext db) : IWorkflowDefinitionRepository
{
    public async Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<WorkflowDefinition?> GetLatestByKeyAsync(Guid tenantId, string key, CancellationToken ct = default)
        => await db.WorkflowDefinitions
            .Where(d => d.TenantId == tenantId && d.Key == key)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync(ct);

    public async Task<WorkflowDefinition?> GetPublishedByKeyAsync(Guid tenantId, string key, CancellationToken ct = default)
        => await db.WorkflowDefinitions
            .Where(d => d.TenantId == tenantId && d.Key == key && d.Status == Domain.Enums.DefinitionStatus.Published)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<WorkflowDefinition>> GetAllByKeyAsync(Guid tenantId, string key, CancellationToken ct = default)
        => await db.WorkflowDefinitions
            .Where(d => d.TenantId == tenantId && d.Key == key)
            .OrderByDescending(d => d.Version)
            .ToListAsync(ct);

    public async Task AddAsync(WorkflowDefinition definition, CancellationToken ct = default)
        => await db.WorkflowDefinitions.AddAsync(definition, ct);

    public async Task SaveAsync(WorkflowDefinition definition, CancellationToken ct = default)
        => await db.SaveChangesAsync(ct);

    public async Task<bool> ExistsByKeyAndVersionAsync(Guid tenantId, string key, int version, CancellationToken ct = default)
        => await db.WorkflowDefinitions.AnyAsync(d => d.TenantId == tenantId && d.Key == key && d.Version == version, ct);
}

internal sealed class EfWorkflowInstanceRepository(WorkflowDbContext db) : IWorkflowInstanceRepository
{
    public async Task<WorkflowInstance?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.WorkflowInstances
            .Include(i => i.Tasks)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<IReadOnlyList<WorkflowInstance>> GetByDocumentAsync(string documentType, Guid documentId, CancellationToken ct = default)
        => await db.WorkflowInstances
            .Include(i => i.Tasks)
            .Where(i => i.DocumentType == documentType && i.DocumentId == documentId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WorkflowInstance>> GetByAssigneeAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
        => await db.WorkflowInstances
            .Include(i => i.Tasks)
            .Where(i => i.TenantId == tenantId && i.Tasks.Any(t => t.AssigneeUserId == userId))
            .ToListAsync(ct);

    public async Task AddAsync(WorkflowInstance instance, CancellationToken ct = default)
        => await db.WorkflowInstances.AddAsync(instance, ct);

    public async Task SaveAsync(WorkflowInstance instance, CancellationToken ct = default)
        => await db.SaveChangesAsync(ct);
}

internal sealed class EfWorkflowTaskRepository(WorkflowDbContext db) : IWorkflowTaskRepository
{
    public async Task<WorkflowTask?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.WorkflowTasks.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<WorkflowTask>> GetByInstanceIdAsync(Guid instanceId, CancellationToken ct = default)
        => await db.WorkflowTasks.Where(t => t.InstanceId == instanceId).ToListAsync(ct);

    public async Task<IReadOnlyList<WorkflowTask>> GetOpenByAssigneeAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
        => await db.WorkflowTasks
            .Where(t => t.AssigneeUserId == userId && t.Status == Domain.Enums.TaskStatus.Open)
            .ToListAsync(ct);

    public async Task AddAsync(WorkflowTask task, CancellationToken ct = default)
        => await db.WorkflowTasks.AddAsync(task, ct);

    public async Task SaveAsync(WorkflowTask task, CancellationToken ct = default)
        => await db.SaveChangesAsync(ct);
}
