namespace Modulus.Inbox.Abstractions;

/// <summary>
/// Configuration for the idempotent integration-event handler and the inbox
/// decorator registered by <c>AddInbox{TContext}()</c>.
/// </summary>
public sealed class InboxOptions
{
    /// <summary>
    /// Maximum number of times a single event will be retried before it is
    /// dead-lettered (the handler stops retrying and logs an error). Defaults
    /// to 5. Set to a large number effectively to disable dead-lettering.
    /// </summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// How long a <see cref="InboxStatus.Processing"/> claim may be held
    /// before it is considered abandoned (the consumer crashed between
    /// claiming and marking the final state) and can be reclaimed by a
    /// redelivery. Defaults to 300 seconds. Without this lease a crashed
    /// consumer wedges the event in <c>Processing</c> forever — every
    /// redelivery defers and the event is never processed nor dead-lettered.
    /// </summary>
    public int ClaimTimeoutSeconds { get; set; } = 300;

    // ── Polly handler retry ────────────────────────────────────────
    // When HandlerRetryCount > 0, each handler invocation is wrapped in a
    // Polly resilience pipeline for fast in-process retries before the
    // inbox's own retry/dead-letter cycle kicks in.

    /// <summary>Number of in-pipeline retries per handler invocation. 0 disables Polly. Default 3.</summary>
    public int HandlerRetryCount { get; set; } = 3;

    /// <summary>Base delay for the first retry. Default 2 seconds.</summary>
    public double HandlerRetryBaseDelaySec { get; set; } = 2;

    /// <summary>Use exponential back-off between retries. Default <c>true</c>.</summary>
    public bool HandlerRetryExponential { get; set; } = true;

    /// <summary>Add jitter to retry delays. Default <c>true</c>.</summary>
    public bool HandlerRetryJitter { get; set; } = true;
}
