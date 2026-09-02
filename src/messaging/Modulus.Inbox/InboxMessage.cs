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

    /// <summary>
    /// When the current <see cref="InboxStatus.Processing"/> claim was taken.
    /// A claim older than the configured timeout (<c>InboxOptions.ClaimTimeoutSeconds</c>)
    /// is treated as abandoned (crashed consumer) and may be reclaimed by a
    /// redelivery. Null for rows not currently claimed (or legacy rows — those
    /// fall back to <see cref="ReceivedAt"/> as the claim time).
    /// </summary>
    public DateTime? ClaimedAt { get; set; }
}

public enum InboxStatus
{
    Pending,
    Processing,
    Processed,
    Failed
}
