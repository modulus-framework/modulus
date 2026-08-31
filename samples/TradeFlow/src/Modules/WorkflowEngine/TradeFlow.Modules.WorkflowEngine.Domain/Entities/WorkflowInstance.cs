using TradeFlow.Modules.WorkflowEngine.Domain.Enums;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.WorkflowEngine.Domain.Entities;

/// <summary>
/// A running workflow instance pinned to a specific definition version.
/// </summary>
public sealed class WorkflowInstance : AggregateRoot
{
    private readonly List<WorkflowTask> _tasks = new();
    private readonly List<WorkflowEvent> _events = new();

    private WorkflowInstance() { }

    private WorkflowInstance(
        Guid id, Guid tenantId, Guid definitionId, string definitionKey,
        int definitionVersion, string documentType, Guid documentId,
        string? contextJson)
    {
        Id = id;
        TenantId = tenantId;
        DefinitionId = definitionId;
        DefinitionKey = definitionKey;
        DefinitionVersion = definitionVersion;
        DocumentType = documentType;
        DocumentId = documentId;
        ContextJson = contextJson;
        State = "Running";
        Status = InstanceStatus.Running;
        StartedAtUtc = DateTime.UtcNow;
    }

    public Guid TenantId { get; private set; }
    public Guid DefinitionId { get; private set; }
    public string DefinitionKey { get; private set; } = null!;
    public int DefinitionVersion { get; private set; }
    public string DocumentType { get; private set; } = null!;
    public Guid DocumentId { get; private set; }
    public string? ContextJson { get; private set; }
    public string State { get; private set; } = default!;
    public InstanceStatus Status { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? CompletedBy { get; private set; }

    public IReadOnlyList<WorkflowTask> Tasks => _tasks;
    public IReadOnlyList<WorkflowEvent> Events => _events;

    public static WorkflowInstance Start(
        Guid tenantId, Guid definitionId, string definitionKey,
        int definitionVersion, string documentType, Guid documentId,
        string? contextJson = null)
    {
        return new WorkflowInstance(
            Guid.NewGuid(), tenantId, definitionId, definitionKey,
            definitionVersion, documentType, documentId, contextJson);
    }

    public void AddTask(WorkflowTask task)
    {
        _tasks.Add(task);
    }

    public void RecordEvent(string eventType, string? payloadJson, string? actor = null)
    {
        _events.Add(new WorkflowEvent(Id, eventType, payloadJson, actor));
    }

    public Result Complete(string completedBy)
    {
        if (Status != InstanceStatus.Running)
            return Result.Failure(Error.BusinessRule("Workflow.Instance.NotRunning", "Only running instances can be completed"));

        Status = InstanceStatus.Completed;
        State = "Completed";
        CompletedAtUtc = DateTime.UtcNow;
        CompletedBy = completedBy;
        return Result.Success();
    }

    public Result Reject(string rejectedBy)
    {
        if (Status != InstanceStatus.Running)
            return Result.Failure(Error.BusinessRule("Workflow.Instance.NotRunning", "Only running instances can be rejected"));

        Status = InstanceStatus.Rejected;
        State = "Rejected";
        CompletedAtUtc = DateTime.UtcNow;
        CompletedBy = rejectedBy;
        return Result.Success();
    }

    public Result Cancel(string cancelledBy)
    {
        if (Status != InstanceStatus.Running)
            return Result.Failure(Error.BusinessRule("Workflow.Instance.NotRunning", "Only running instances can be cancelled"));

        Status = InstanceStatus.Cancelled;
        State = "Cancelled";
        CompletedAtUtc = DateTime.UtcNow;
        CompletedBy = cancelledBy;
        return Result.Success();
    }
}
