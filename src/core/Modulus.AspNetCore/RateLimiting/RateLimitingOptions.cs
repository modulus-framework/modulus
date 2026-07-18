namespace Modulus.AspNetCore.RateLimiting;

/// <summary>
/// How incoming requests are grouped into independent rate-limit buckets.
/// </summary>
public enum RateLimitPartitionStrategy
{
    /// <summary>One shared bucket for the whole application.</summary>
    Global,

    /// <summary>A bucket per remote IP address (falls back to Global if unknown).</summary>
    IpAddress,

    /// <summary>A bucket per authenticated user (falls back to IP for anonymous callers).</summary>
    User,

    /// <summary>A bucket per tenant (falls back to IP when no tenant is in scope).</summary>
    Tenant,
}

/// <summary>
/// Binds from the <c>RateLimiting</c> configuration section. Backs
/// <see cref="RateLimitingExtensions.AddModulusRateLimiting"/> — a fixed-window
/// limiter partitioned by <see cref="Partition"/>.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>When false, the limiter middleware is a no-op passthrough.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Requests allowed per <see cref="WindowSeconds"/> per partition.</summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>Length of the fixed window, in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Requests queued once the permit limit is hit (0 = reject immediately).</summary>
    public int QueueLimit { get; set; }

    /// <summary>How callers are grouped into buckets.</summary>
    public RateLimitPartitionStrategy Partition { get; set; } = RateLimitPartitionStrategy.User;

    /// <summary>HTTP status returned when a request is rejected. Defaults to 429.</summary>
    public int RejectionStatusCode { get; set; } = 429;
}
