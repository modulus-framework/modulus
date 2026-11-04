namespace Modulus.Analytics.EntityFrameworkCore;

/// <summary>Backfill settings, bound from the <c>Analytics:Backfill</c> configuration section.</summary>
public sealed class AnalyticsBackfillOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Analytics:Backfill";

    /// <summary>
    /// Runs the background worker. Turn it off on replicas that should not read the operational store for backfills (for example the
    /// web replicas, keeping the work on a worker replica). Defaults to <see langword="true"/>.
    /// </summary>
    public bool EnableWorker { get; set; } = true;

    /// <summary>Delay between cycles when no run was pending. Defaults to 10 seconds.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Records read per page. Defaults to 1000.</summary>
    public int PageSize { get; set; } = 1000;

    /// <summary>Pages one run reads per cycle before it yields to the next run. Defaults to 50.</summary>
    public int PagesPerCycle { get; set; } = 50;

    /// <summary>Runs claimed per cycle. Defaults to 1, so one worker reads one tenant at a time.</summary>
    public int RunsPerCycle { get; set; } = 1;

    /// <summary>How long a claim is held. A worker that crashes releases its run once it expires. Defaults to 5 minutes.</summary>
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Failed cycles in a row before the run is marked failed. Defaults to 5.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>The wait after a failed cycle is this delay times the attempt number. Defaults to 30 seconds.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);
}
