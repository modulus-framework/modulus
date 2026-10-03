namespace Modulus.Webhooks;

/// <summary>Webhook settings, bound from the <c>Webhooks</c> configuration section.</summary>
public sealed class ModulusWebhooksOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Webhooks";

    /// <summary>
    /// Runs the background delivery worker. Turn it off on replicas that should only record deliveries (or in tests);
    /// events are still fanned out to subscriptions and wait for a replica that delivers.
    /// </summary>
    public bool EnableDelivery { get; set; } = true;

    /// <summary>Delay between delivery cycles. Defaults to 5 seconds.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Deliveries claimed per cycle. Defaults to 50.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Requests sent at the same time within a cycle. Defaults to 8.</summary>
    public int MaxConcurrency { get; set; } = 8;

    /// <summary>
    /// How long a claim is held. A replica that crashes mid-batch releases its deliveries once it expires; keep it above
    /// <see cref="RequestTimeout"/> times the batch size divided by <see cref="MaxConcurrency"/>. Defaults to 5 minutes.
    /// </summary>
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Time allowed for one request, response included. Defaults to 15 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>The Standard Webhooks retry schedule: 5 s, 5 min, 30 min, 2 h, 5 h, 10 h, 10 h.</summary>
    public static IReadOnlyList<TimeSpan> DefaultRetrySchedule { get; } =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(5),
        TimeSpan.FromHours(10),
        TimeSpan.FromHours(10),
    ];

    /// <summary>
    /// Delay before each retry of a failed delivery; a delivery is dead-lettered once the schedule is used up (so the
    /// number of attempts is its length plus one). Null (the default) uses <see cref="DefaultRetrySchedule"/>; an empty
    /// list never retries. A <c>Retry-After</c> header longer than the next delay (up to a day) is honored.
    /// </summary>
    /// <remarks>Null rather than a pre-filled list: the configuration binder appends to an existing list.</remarks>
    public IList<TimeSpan>? RetrySchedule { get; set; }

    internal IReadOnlyList<TimeSpan> EffectiveRetrySchedule
        => RetrySchedule is null ? DefaultRetrySchedule : [.. RetrySchedule];

    /// <summary>
    /// A subscription whose endpoint has failed every attempt for this long is disabled (its pending deliveries are then
    /// dead-lettered; re-enabling it and retrying them resumes delivery). Null never disables. Defaults to 5 days.
    /// </summary>
    public TimeSpan? DisableAfterFailingFor { get; set; } = TimeSpan.FromDays(5);

    /// <summary>
    /// How long the previous secret keeps signing (next to the new one) after a rotation, so receivers can switch
    /// without dropping deliveries. Defaults to 24 hours.
    /// </summary>
    public TimeSpan SecretRotationOverlap { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Delivered and dead-lettered deliveries older than this are deleted. Null keeps them. Defaults to 30 days.</summary>
    public TimeSpan? PurgeAfter { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Only the replica holding the <c>IDistributedLock</c> lease runs a delivery cycle (claims already keep replicas
    /// from sending the same delivery twice; this saves the redundant polling). Needs an <c>IDistributedLock</c>.
    /// </summary>
    public bool EnableLeaderElection { get; set; }

    /// <summary>Accepts <c>http://</c> endpoint URLs. Development only: payloads and signatures travel in clear text.</summary>
    public bool AllowHttp { get; set; }

    /// <summary>
    /// Allows endpoints on loopback, private, link-local and other non-public addresses. Off, every connection is checked
    /// after DNS resolution (so a public name that resolves to an internal address is refused too) and proxies are
    /// bypassed: subscribers cannot make the server call into its own network (SSRF). Development only.
    /// </summary>
    public bool AllowPrivateNetworks { get; set; }

    /// <summary>The most subscriptions one tenant may create. Defaults to 100.</summary>
    public int MaxSubscriptionsPerTenant { get; set; } = 100;

    /// <summary>Characters of an endpoint's error response kept on a failed delivery. Defaults to 1024.</summary>
    public int MaxStoredResponseLength { get; set; } = 1024;

    /// <summary>The <c>User-Agent</c> of delivery requests.</summary>
    public string UserAgent { get; set; } = "Modulus-Webhooks/1.0";
}
