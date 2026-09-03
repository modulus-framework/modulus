namespace Modulus.Inbox.Abstractions;

public sealed class InboxMessage
{
    public Guid Id { get; init; }  // = integration event EventId

    /// <summary>
    /// Identity of the handler this claim belongs to (the inner handler's
    /// <c>Type.FullName</c>), so two handlers subscribed to the same event
    /// claim independent rows instead of racing over one. The PK is
    /// <c>(Id, HandlerName)</c>.
    /// <para>
    /// <b>Legacy rows</b> — written before this column existed — have
    /// <see cref="string.Empty"/> here (the DB column default). They are
    /// honoured for ANY handler claiming that <see cref="Id"/>: a legacy
    /// <c>Processed</c>/dead-lettered row is treated as already handled
    /// (skipped) rather than re-run, and a legacy row still eligible to claim
    /// is "adopted" (its <see cref="HandlerName"/> is set to the claiming
    /// handler) the first time some handler claims it. See
    /// <c>EfInboxStore</c>/<c>MongoInboxStore</c> for the adoption logic.
    /// </para>
    /// </summary>
    public string HandlerName { get; init; } = string.Empty;

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
