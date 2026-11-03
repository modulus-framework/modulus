namespace Modulus.Analytics.EntityFrameworkCore;

/// <summary>One captured analytics record waiting for, or finished with, delivery.</summary>
public sealed class AnalyticsDelivery
{
    /// <summary>The row id.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The record's tenant.</summary>
    public Guid TenantId { get; init; }

    /// <summary>The record's id (the integration event id). With <see cref="TenantId"/> and <see cref="Dataset"/> it is unique, so a redelivered event queues once.</summary>
    public Guid EventId { get; init; }

    /// <summary>The dataset name.</summary>
    public string Dataset { get; init; } = string.Empty;

    /// <summary>The dataset version the stored values follow.</summary>
    public int SchemaVersion { get; init; }

    /// <summary>When the business fact happened (UTC).</summary>
    public DateTime OccurredAt { get; init; }

    /// <summary>The values as JSON, typed by the dataset version (see <see cref="AnalyticsValueSerializer"/>).</summary>
    public string Payload { get; init; } = string.Empty;

    /// <summary>When the row was queued (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>When every sink accepted it (UTC).</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>When it was given up on (UTC); a retry from management sends it again.</summary>
    public DateTime? DeadLetteredAt { get; set; }

    /// <summary>Attempts that failed so far.</summary>
    public int AttemptCount { get; set; }

    /// <summary>The earliest time of the next attempt; <see langword="null"/> means as soon as possible.</summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>The worker holding the claim.</summary>
    public string? LockedBy { get; set; }

    /// <summary>When the claim expires.</summary>
    public DateTime? LockedUntil { get; set; }

    /// <summary>When the last attempt was made.</summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>What went wrong on the last failed attempt (truncated).</summary>
    public string? LastError { get; set; }

    /// <summary>Pending, delivered or failed.</summary>
    public AnalyticsDeliveryStatus Status => DeliveredAt is not null
        ? AnalyticsDeliveryStatus.Delivered
        : DeadLetteredAt is not null ? AnalyticsDeliveryStatus.Failed : AnalyticsDeliveryStatus.Pending;
}

/// <summary>Where a delivery stands.</summary>
public enum AnalyticsDeliveryStatus
{
    /// <summary>Waiting for its first attempt or a retry.</summary>
    Pending,

    /// <summary>Accepted by every sink.</summary>
    Delivered,

    /// <summary>Given up on (dead-lettered).</summary>
    Failed,
}
