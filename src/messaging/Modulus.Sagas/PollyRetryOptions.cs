using global::Polly;

namespace Modulus.Sagas;

/// <summary>
/// Polly v8 resilience options used across the Rebus pipeline step, the
/// outbox dispatcher, and the inbox handler decorator.  Configures retry
/// (with optional exponential back-off and jitter) and an optional circuit
/// breaker.
/// </summary>
public sealed class PollyRetryOptions
{
    /// <summary>
    /// Maximum retry attempts per invocation.  <c>0</c> disables retry.
    /// Default <c>3</c>.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay before the first retry.  With
    /// <see cref="DelayBackoffType.Exponential"/> the actual delay is
    /// <c>BaseDelay * 2^attempt</c> (plus jitter when
    /// <see cref="UseJitter"/> is <c>true</c>).  Default 2 seconds.
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Back-off strategy.  Default <see cref="DelayBackoffType.Exponential"/>.
    /// </summary>
    public DelayBackoffType DelayBackoffType { get; set; } = DelayBackoffType.Exponential;

    /// <summary>Add random jitter to retry delays to avoid thundering-herd. Default <c>true</c>.</summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>Enable circuit-breaker protection. Default <c>false</c>.</summary>
    public bool EnableCircuitBreaker { get; set; }

    /// <summary>Failure ratio that opens the breaker. Default <c>0.5</c> (50%).</summary>
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    /// <summary>Sampling window for failure-rate calculation. Default 30 s.</summary>
    public TimeSpan CircuitBreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Minimum actions in the sampling window before the breaker can open. Default 8.</summary>
    public int CircuitBreakerMinimumThroughput { get; set; } = 8;

    /// <summary>How long the breaker stays open before half-opening. Default 30 s.</summary>
    public TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Convenience factory.</summary>
public static class PollyRetry
{
    /// <summary>Default retry options (3 attempts, exponential, no breaker).</summary>
    public static PollyRetryOptions Default() => new();

    /// <summary>No retry — delegates straight through.</summary>
    public static PollyRetryOptions None() => new() { MaxRetryAttempts = 0 };
}
