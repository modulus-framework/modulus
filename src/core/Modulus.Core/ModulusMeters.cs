namespace Modulus.Observability;

using System.Diagnostics.Metrics;

/// <summary>
/// Metric instruments for Modulus subsystems. Each meter is scoped to a feature area.
/// Use the MeterProviderBuilder's AddMeter() to register these meters with the
/// host's OpenTelemetry metrics provider.
/// </summary>
public static class ModulusMeters
{
    private const string Version = "1.0.0";

    public static readonly Meter Outbox = new("Modulus.Outbox", Version);
    public static readonly Meter Inbox = new("Modulus.Inbox", Version);
    public static readonly Meter Events = new("Modulus.Events", Version);
    public static readonly Meter BackgroundJobs = new("Modulus.BackgroundJobs", Version);
    public static readonly Meter RateLimiting = new("Modulus.RateLimiting", Version);
    public static readonly Meter MultiTenancy = new("Modulus.MultiTenancy", Version);

    public static readonly string[] AllMeters =
    [
        Outbox.Name,
        Inbox.Name,
        Events.Name,
        BackgroundJobs.Name,
        RateLimiting.Name,
        MultiTenancy.Name,
    ];

    /// <summary>Outbox: count of messages dispatched successfully.</summary>
    public static readonly Counter<long> OutboxDispatched = Outbox.CreateCounter<long>(
        "modulus.outbox.dispatched",
        unit: "1",
        description: "Count of outbox messages successfully dispatched");

    /// <summary>Outbox: count of messages dead-lettered after max retries.</summary>
    public static readonly Counter<long> OutboxDeadLettered = Outbox.CreateCounter<long>(
        "modulus.outbox.dead_lettered",
        unit: "1",
        description: "Count of outbox messages dead-lettered after exhausting retries");

    /// <summary>Outbox: current depth (pending messages).</summary>
    public static readonly UpDownCounter<long> OutboxDepth = Outbox.CreateUpDownCounter<long>(
        "modulus.outbox.depth",
        unit: "1",
        description: "Current count of pending outbox messages");

    /// <summary>Inbox: count of duplicate message attempts (dedup hits).</summary>
    public static readonly Counter<long> InboxDedupHits = Inbox.CreateCounter<long>(
        "modulus.inbox.dedup_hits",
        unit: "1",
        description: "Count of integration events skipped due to previous processing (dedup)");

    /// <summary>Inbox: count of messages dead-lettered after max retries.</summary>
    public static readonly Counter<long> InboxDeadLettered = Inbox.CreateCounter<long>(
        "modulus.inbox.dead_lettered",
        unit: "1",
        description: "Count of inbox messages dead-lettered after exhausting retries");

    /// <summary>Event bus: count of publishes the broker returned as unroutable (mandatory + basic.return).</summary>
    public static readonly Counter<long> EventsUnroutable = Events.CreateCounter<long>(
        "modulus.events.unroutable",
        unit: "1",
        description: "Count of integration event publishes the broker could not route (basic.return)");

    /// <summary>Background jobs: count of jobs started.</summary>
    public static readonly Counter<long> JobsStarted = BackgroundJobs.CreateCounter<long>(
        "modulus.jobs.started",
        unit: "1",
        description: "Count of background jobs started");

    /// <summary>Background jobs: count of jobs completed successfully.</summary>
    public static readonly Counter<long> JobsCompleted = BackgroundJobs.CreateCounter<long>(
        "modulus.jobs.completed",
        unit: "1",
        description: "Count of background jobs completed successfully");

    /// <summary>Background jobs: count of jobs failed.</summary>
    public static readonly Counter<long> JobsFailed = BackgroundJobs.CreateCounter<long>(
        "modulus.jobs.failed",
        unit: "1",
        description: "Count of background jobs that threw an unhandled exception");

    /// <summary>Background jobs: current queue depth (enqueued but not yet started).</summary>
    public static readonly UpDownCounter<long> JobsQueueDepth = BackgroundJobs.CreateUpDownCounter<long>(
        "modulus.jobs.queue_depth",
        unit: "1",
        description: "Current count of enqueued background jobs waiting for a worker");

    /// <summary>Background jobs: current count of recurring job registrations.</summary>
    public static readonly UpDownCounter<long> RecurringJobCount = BackgroundJobs.CreateUpDownCounter<long>(
        "modulus.jobs.recurring_count",
        unit: "1",
        description: "Current count of registered recurring jobs");

    /// <summary>Rate limiting: count of rate-limit rejections.</summary>
    public static readonly Counter<long> RateLimitRejected = RateLimiting.CreateCounter<long>(
        "modulus.rate_limit.rejected",
        unit: "1",
        description: "Count of requests rejected due to rate limit");

    /// <summary>Rate limiting: current count of active partitions.</summary>
    public static readonly UpDownCounter<long> RateLimitPartitions = RateLimiting.CreateUpDownCounter<long>(
        "modulus.rate_limit.partitions",
        unit: "1",
        description: "Current count of active rate-limit partitions");

    /// <summary>Multi-tenancy: count of requests with no tenant resolved (fail-closed path).</summary>
    public static readonly Counter<long> UnresolvedTenant = MultiTenancy.CreateCounter<long>(
        "modulus.tenant.unresolved",
        unit: "1",
        description: "Count of requests where tenant resolution was required but failed");
}
