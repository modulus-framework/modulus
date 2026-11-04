namespace Modulus.Analytics.EntityFrameworkCore;

/// <summary>Where a backfill run stands.</summary>
public enum AnalyticsBackfillStatus
{
    /// <summary>Waiting for a worker.</summary>
    Pending = 0,

    /// <summary>Being read, or between cycles with more pages to go.</summary>
    Running = 1,

    /// <summary>The source has no more rows; everything it returned is queued.</summary>
    Completed = 2,

    /// <summary>Given up on (a permanent error, or the attempts were used up). Restart it from management.</summary>
    Failed = 3,
}

/// <summary>
/// One tenant's backfill of one dataset version. The cursor is saved in the same transaction that queues each page, so a crash or a
/// restart resumes after the last queued page and neither loses nor repeats rows.
/// </summary>
public sealed class AnalyticsBackfillRun
{
    /// <summary>The run id.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The dataset name.</summary>
    public string Dataset { get; init; } = string.Empty;

    /// <summary>The dataset version.</summary>
    public int SchemaVersion { get; init; }

    /// <summary>The tenant being read.</summary>
    public Guid TenantId { get; init; }

    /// <summary>The state of the run.</summary>
    public AnalyticsBackfillStatus Status { get; set; }

    /// <summary>Where the last queued page ended; <see langword="null"/> before the first page.</summary>
    public string? Cursor { get; set; }

    /// <summary>Records queued so far.</summary>
    public long RowsQueued { get; set; }

    /// <summary>Consecutive failed cycles. Reset by any page that succeeds.</summary>
    public int AttemptCount { get; set; }

    /// <summary>When the run was created (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>When the run last made progress or changed state (UTC).</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>When the run completed (UTC).</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>The earliest time of the next attempt after a failure.</summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>The worker holding the claim.</summary>
    public string? LockedBy { get; set; }

    /// <summary>When the claim expires; another worker then takes the run over.</summary>
    public DateTime? LockedUntil { get; set; }

    /// <summary>What went wrong last (truncated).</summary>
    public string? LastError { get; set; }

    /// <summary>Who started the run, for the record.</summary>
    public string? RequestedBy { get; set; }
}
