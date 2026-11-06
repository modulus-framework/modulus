namespace Modulus.Analytics;

using Microsoft.Extensions.Options;

/// <summary>Settings for the per-sink circuit breaker (<c>Analytics:CircuitBreaker</c> when bound from configuration).</summary>
public sealed class AnalyticsCircuitBreakerOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Analytics:CircuitBreaker";

    /// <summary>Consecutive transient failures of one sink that open its circuit. Must be positive.</summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>How long an open circuit refuses writes before one trial write is allowed. Must be positive.</summary>
    public TimeSpan OpenDuration { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Stops the dispatcher from calling a sink that keeps failing. After <see cref="AnalyticsCircuitBreakerOptions.FailureThreshold"/>
/// consecutive transient failures the circuit opens for <see cref="AnalyticsCircuitBreakerOptions.OpenDuration"/>: writes to that sink
/// are refused at once and their records stay queued for a later attempt. Then one trial write is let through (half-open). A success
/// closes the circuit; a transient failure reopens it.
/// </summary>
/// <remarks>
/// A permanent failure (bad credentials, a type the table rejects) shows the sink is reachable, so it closes the circuit rather than
/// counting against it: those records are dead-lettered, and retrying them later would not help. Each sink has its own circuit.
/// </remarks>
public sealed class AnalyticsCircuitBreaker
{
    private readonly object _gate = new();
    private readonly Dictionary<string, SinkState> _states = new(StringComparer.Ordinal);
    private readonly IOptions<AnalyticsCircuitBreakerOptions> _options;
    private readonly TimeProvider _clock;

    /// <summary>Creates the breaker.</summary>
    /// <param name="options">The thresholds.</param>
    /// <param name="clock">The clock used for the open period.</param>
    public AnalyticsCircuitBreaker(IOptions<AnalyticsCircuitBreakerOptions> options, TimeProvider clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>Whether the sink's circuit is open right now, so writes are refused.</summary>
    /// <param name="sink">The sink name.</param>
    public bool IsOpen(string sink)
    {
        lock (_gate)
        {
            return _states.TryGetValue(sink, out var state) && state.OpenUntil is { } until && _clock.GetUtcNow() < until;
        }
    }

    /// <summary>
    /// Asks to write to <paramref name="sink"/>. Returns false while the circuit is open, or while a trial write is already in flight.
    /// When the open period has passed, the first caller gets the trial write.
    /// </summary>
    /// <param name="sink">The sink name.</param>
    public bool TryEnter(string sink)
    {
        lock (_gate)
        {
            var state = StateOf(sink);
            if (state.OpenUntil is null)
            {
                return true;
            }

            if (_clock.GetUtcNow() < state.OpenUntil || state.TrialInFlight)
            {
                return false;
            }

            state.TrialInFlight = true;
            return true;
        }
    }

    /// <summary>Records a write that completed, or failed permanently (the sink answered), and closes the circuit.</summary>
    /// <param name="sink">The sink name.</param>
    public void RecordSuccess(string sink)
    {
        lock (_gate)
        {
            var state = StateOf(sink);
            state.Failures = 0;
            state.OpenUntil = null;
            state.TrialInFlight = false;
        }
    }

    /// <summary>Records a transient failure. Opens the circuit at the threshold, and reopens it after a failed trial write.</summary>
    /// <param name="sink">The sink name.</param>
    public void RecordTransientFailure(string sink)
    {
        lock (_gate)
        {
            var options = _options.Value;
            var state = StateOf(sink);
            state.TrialInFlight = false;
            state.Failures++;
            if (state.Failures >= options.FailureThreshold || state.OpenUntil is not null)
            {
                state.OpenUntil = _clock.GetUtcNow() + options.OpenDuration;
            }
        }
    }

    /// <summary>Ends a write that was cancelled before the sink answered. It says nothing about the sink, so only a trial in flight is released.</summary>
    /// <param name="sink">The sink name.</param>
    public void Release(string sink)
    {
        lock (_gate)
        {
            StateOf(sink).TrialInFlight = false;
        }
    }

    private SinkState StateOf(string sink)
    {
        if (!_states.TryGetValue(sink, out var state))
        {
            state = new SinkState();
            _states[sink] = state;
        }

        return state;
    }

    private sealed class SinkState
    {
        public int Failures { get; set; }

        public DateTimeOffset? OpenUntil { get; set; }

        public bool TrialInFlight { get; set; }
    }
}
