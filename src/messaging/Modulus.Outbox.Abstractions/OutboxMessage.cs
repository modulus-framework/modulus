namespace Modulus.Outbox.Abstractions;

public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string MessageType { get; init; } = default!;
    public string Payload { get; init; } = default!;
    public Guid TenantId { get; init; }
    public string ModuleName { get; init; } = default!;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public string? LockedBy { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public int RetryCount { get; set; }
    public string? Error { get; set; }
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public string? TraceParent { get; init; }
    public string? TraceState { get; init; }
}
