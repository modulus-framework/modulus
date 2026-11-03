namespace Modulus.Analytics.EntityFrameworkCore;

/// <summary>Analytics delivery settings, bound from the <c>Analytics:Delivery</c> configuration section.</summary>
public sealed class AnalyticsDeliveryOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Analytics:Delivery";

    /// <summary>
    /// Runs the background worker. Turn it off on replicas that should only queue records (or in tests); records wait for a replica
    /// that delivers.
    /// </summary>
    public bool EnableDelivery { get; set; } = true;

    /// <summary>Delay between cycles when the last one was not full. Defaults to 5 seconds.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Rows claimed per cycle. Defaults to 500.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// How long a claim is held. A replica that crashes mid-batch releases its rows once it expires; keep it above the time a batch
    /// takes to write. Defaults to 5 minutes.
    /// </summary>
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The wait before each retry after a transient failure. When the schedule is used up the row is dead-lettered. Defaults to
    /// 5 s, 30 s, 2 min, 10 min, 1 h and 6 h.
    /// </summary>
    public TimeSpan[] RetrySchedule { get; set; } =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(6),
    ];

    /// <summary>How long delivered rows are kept before they are purged. Defaults to 7 days. Dead letters are never purged automatically.</summary>
    public TimeSpan PurgeDeliveredAfter { get; set; } = TimeSpan.FromDays(7);
}
