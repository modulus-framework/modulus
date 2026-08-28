using ProcureFlow.Modules.WorkflowEngine.Domain.Enums;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.WorkflowEngine.Domain.Entities;

/// <summary>
/// Versioned workflow definition stored as JSON. In-flight instances are pinned to their version forever.
/// </summary>
public sealed class WorkflowDefinition : AggregateRoot
{
    private WorkflowDefinition() { }

    private WorkflowDefinition(
        Guid id, Guid tenantId, string key, string name, int version,
        string documentType, string triggerEvent, string stepsJson,
        string? contextSchemaJson, string? onRejectJson, string? onTimeoutAction)
    {
        Id = id;
        TenantId = tenantId;
        Key = key;
        Name = name;
        Version = version;
        DocumentType = documentType;
        TriggerEvent = triggerEvent;
        StepsJson = stepsJson;
        ContextSchemaJson = contextSchemaJson;
        OnRejectJson = onRejectJson;
        OnTimeoutAction = onTimeoutAction;
        Status = DefinitionStatus.Draft;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid TenantId { get; private set; }
    public string Key { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public int Version { get; private set; }
    public string DocumentType { get; private set; } = null!;
    public string TriggerEvent { get; private set; } = null!;
    public string StepsJson { get; private set; } = null!;
    public string? ContextSchemaJson { get; private set; }
    public string? OnRejectJson { get; private set; }
    public string? OnTimeoutAction { get; private set; }
    public DefinitionStatus Status { get; private set; }
    public string? PublishedBy { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static WorkflowDefinition CreateDraft(
        Guid tenantId, string key, string name, string documentType,
        string triggerEvent, string stepsJson,
        string? contextSchemaJson = null, string? onRejectJson = null, string? onTimeoutAction = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key is required", nameof(key));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required", nameof(name));
        if (string.IsNullOrWhiteSpace(stepsJson))
            throw new ArgumentException("Steps JSON is required", nameof(stepsJson));

        return new WorkflowDefinition(
            Guid.NewGuid(), tenantId, key.Trim(), name.Trim(), 1,
            documentType?.Trim() ?? "unknown", triggerEvent?.Trim() ?? "*",
            stepsJson, contextSchemaJson, onRejectJson, onTimeoutAction);
    }

    public static WorkflowDefinition CreateNextVersion(WorkflowDefinition current, string stepsJson)
    {
        if (current.Status != DefinitionStatus.Draft)
            throw new InvalidOperationException("Can only create next version from a Draft definition");

        return new WorkflowDefinition(
            Guid.NewGuid(), current.TenantId, current.Key, current.Name, current.Version + 1,
            current.DocumentType, current.TriggerEvent, stepsJson,
            current.ContextSchemaJson, current.OnRejectJson, current.OnTimeoutAction);
    }

    public Result Publish(string publishedBy)
    {
        if (Status != DefinitionStatus.Draft)
            return Result.Failure(Error.BusinessRule("Workflow.Definition.NotDraft", "Only Draft definitions can be published"));
        if (string.IsNullOrWhiteSpace(publishedBy))
            return Result.Failure(Error.Validation("Workflow.Definition.NoPublisher", "PublishedBy is required"));

        Status = DefinitionStatus.Published;
        PublishedBy = publishedBy;
        PublishedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Retire(string retiredBy)
    {
        if (Status != DefinitionStatus.Published)
            return Result.Failure(Error.BusinessRule("Workflow.Definition.NotPublished", "Only Published definitions can be retired"));

        Status = DefinitionStatus.Retired;
        return Result.Success();
    }
}
