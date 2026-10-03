namespace Modulus.Caching;

/// <summary>
/// Settings for the FusionCache-backed hybrid cache, bound from
/// <c>Caching:Fusion</c> by <c>AddModulusFusionCache</c>.
/// </summary>
public sealed class ModulusFusionCacheOptions
{
    public const string SectionName = "Caching:Fusion";

    /// <summary>
    /// Name of this cache; also the key prefix in a shared distributed cache and
    /// the backplane channel prefix. In a microservice deployment give every
    /// service its own name so services sharing one Redis never read or
    /// invalidate each other's entries.
    /// </summary>
    public string CacheName { get; set; } = "modulus";

    /// <summary>Lifetime of an entry when the caller passes no expiry.</summary>
    public TimeSpan DefaultDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// When the factory fails (database down, timeout), serve the last good
    /// value instead of throwing, for at most <see cref="FailSafeMaxDuration"/>.
    /// </summary>
    public bool FailSafeEnabled { get; set; } = true;

    /// <summary>How long a stale value may be kept for fail-safe.</summary>
    public TimeSpan FailSafeMaxDuration { get; set; } = TimeSpan.FromHours(2);

    /// <summary>How long a fail-safe value is served before the factory is retried.</summary>
    public TimeSpan FailSafeThrottleDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When a stale value exists, a factory slower than this returns the stale
    /// value and keeps running in the background. Null = no soft timeout.
    /// </summary>
    public TimeSpan? FactorySoftTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Absolute factory timeout, even with no stale value. Null = none.</summary>
    public TimeSpan? FactoryHardTimeout { get; set; }

    /// <summary>
    /// Fraction of the duration (0..1, exclusive) after which a hit triggers a
    /// background refresh, so hot keys never expire under load. Null = off.
    /// </summary>
    public float? EagerRefreshThreshold { get; set; } = 0.9f;

    /// <summary>
    /// Random extra lifetime (0..value) added per entry so entries cached at the
    /// same moment do not all expire at the same moment. Null = none.
    /// </summary>
    public TimeSpan? JitterMaxDuration { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Time to wait for the distributed cache before falling back to L1 only
    /// (when a value is available there). Null = no limit.
    /// </summary>
    public TimeSpan? DistributedCacheSoftTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Hard limit for a distributed cache operation. Null = none.</summary>
    public TimeSpan? DistributedCacheHardTimeout { get; set; } = TimeSpan.FromSeconds(2);
}
