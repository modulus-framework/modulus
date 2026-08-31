namespace TradeFlow.Modules.WorkflowEngine.Domain.Entities;

/// <summary>
/// Append-only audit event for workflow instance transitions (event-sourced-ish).
/// </summary>
public sealed class WorkflowEvent
{
    private WorkflowEvent() { }

    internal WorkflowEvent(Guid instanceId, string eventType, string? payloadJson, string? actor)
    {
        Id = Guid.NewGuid();
        InstanceId = instanceId;
        EventType = eventType;
        PayloadJson = payloadJson;
        Actor = actor;
        OccurredAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid InstanceId { get; private set; }
    public string EventType { get; private set; } = null!;
    public string? PayloadJson { get; private set; }
    public string? Actor { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
}
