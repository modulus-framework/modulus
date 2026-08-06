namespace Modulus.Inbox.Abstractions;

public sealed class InboxMessage
{
    public Guid Id { get; init; }  // = integration event EventId
    public string MessageType { get; init; } = default!;
    public string Payload { get; init; } = default!;
    public Guid TenantId { get; init; }
    public string ModuleName { get; init; } = default!;
    public DateTime ReceivedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public InboxStatus Status { get; set; } = InboxStatus.Pending;
    public string? Error { get; set; }
    public int RetryCount { get; set; }
    public string? CorrelationId { get; init; }
}

public enum InboxStatus
{
    Pending,
    Processing,
    Processed,
    Failed
}
