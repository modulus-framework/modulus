namespace Modulus.Outbox.Abstractions;

/// <summary>
/// Runtime tuning for the transactional outbox processor.
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>Rows claimed per polling cycle. Defaults to 100.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Failed dispatches are dead-lettered after this many attempts. Defaults to 5.</summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>Delay between background polling cycles, in seconds. Defaults to 5.</summary>
    public int PollingIntervalSec { get; set; } = 5;

    /// <summary>How long a claim is held before a crashed instance's lock expires, in seconds. Defaults to 30.</summary>
    public int LockTimeoutSec { get; set; } = 30;

    /// <summary>First retry delay for a failed dispatch, in seconds; doubles per attempt. Defaults to 2.</summary>
    public int InitialBackoffSec { get; set; } = 2;

    /// <summary>Dispatcher used to relay dispatched messages. Defaults to "in-process".</summary>
    public string Dispatcher { get; set; } = "in-process";

    /// <summary>Skip registering the automatic polling hosted service. Defaults to false.</summary>
    public bool DisableAutoPolling { get; set; } = false;

    /// <summary>
    /// When enabled, the polling service acquires a distributed lock
    /// before each processing cycle. Only the replica that holds the lock
    /// polls the outbox; others idle until the lease expires. Prevents
    /// redundant cross-replica polling while the row-level claim already
    /// provides correctness. Requires an <c>IDistributedLock</c>
    /// implementation (e.g. Redis). Defaults to false.
    /// </summary>
    public bool EnableLeaderElection { get; set; } = false;

    /// <summary>
    /// Retention window, in days, after which dispatched rows (and dead-lettered
    /// rows past MaxRetries) are deleted from outbox_messages by the processor.
    /// Housekeeping runs in bounded batches so long-lived tables don't grow
    /// without end. 0 disables purging. Defaults to 7.
    /// </summary>
    public int PurgeAfterDays { get; set; } = 7;
}
