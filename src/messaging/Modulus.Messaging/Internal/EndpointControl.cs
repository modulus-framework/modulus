namespace Modulus.Messaging.Internal;

/// <summary>
/// Admission control for one endpoint: the rate limit, the kill switch and the circuit breaker. Every message waits here
/// before its first attempt, and each final outcome is recorded here so the kill switch and the breaker can trip.
/// </summary>
internal sealed class EndpointControl
{
    private readonly RateLimit? _rate;
    private readonly KillSwitchOptions? _killSwitch;
    private readonly CircuitBreakerOptions? _breaker;
    private readonly Queue<bool> _window = new();
    private readonly Queue<DateTimeOffset> _admissions = new();
    private readonly object _gate = new();
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;
    private int _failures;
    private CircuitState _circuit = CircuitState.Closed;
    private DateTimeOffset _openUntil = DateTimeOffset.MinValue;
    private bool _trialInFlight;
    private int _consecutiveFailures;

    internal EndpointControl(ConsumerOptions options)
    {
        _rate = options.RateLimit;
        _killSwitch = options.KillSwitch?.Clone();
        _breaker = options.CircuitBreaker?.Clone();
    }

    private enum CircuitState
    {
        Closed,
        Open,
        HalfOpen,
    }

    /// <summary>Waits out a kill-switch pause and an open circuit, then waits for a rate-limit slot.</summary>
    internal async Task Admit(CancellationToken cancellationToken)
    {
        while (true)
        {
            var wait = TimeSpan.Zero;
            lock (_gate)
            {
                var now = DateTimeOffset.UtcNow;
                if (_pausedUntil > now)
                {
                    wait = _pausedUntil - now;
                }
                else if (_breaker is not null && _circuit != CircuitState.Closed)
                {
                    if (_circuit == CircuitState.Open && now >= _openUntil)
                    {
                        _circuit = CircuitState.HalfOpen;
                        _trialInFlight = false;
                    }

                    if (_circuit == CircuitState.Open)
                    {
                        wait = _openUntil - now;
                    }
                    else if (!_trialInFlight)
                    {
                        // Half-open: exactly one trial message goes ahead; its outcome decides the circuit.
                        _trialInFlight = true;
                        return;
                    }
                    else
                    {
                        wait = TimeSpan.FromMilliseconds(20);
                    }
                }
                else if (_rate is null)
                {
                    return;
                }
                else
                {
                    // Sliding window: at most Messages admissions within Period.
                    while (_admissions.Count > 0 && _admissions.Peek() <= now - _rate.Period)
                    {
                        _admissions.Dequeue();
                    }

                    if (_admissions.Count < _rate.Messages)
                    {
                        _admissions.Enqueue(now);
                        return;
                    }

                    wait = _admissions.Peek() + _rate.Period - now;
                }
            }

            await Task.Delay(wait < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : wait, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Records the final outcome of one message, pauses the endpoint if the kill switch trips and opens the circuit if the
    /// breaker trips. Returns how long the endpoint is held back by this outcome, or null when it is not.
    /// </summary>
    internal TimeSpan? Record(bool succeeded)
    {
        lock (_gate)
        {
            TimeSpan? held = null;
            var now = DateTimeOffset.UtcNow;

            if (_killSwitch is not null)
            {
                _window.Enqueue(succeeded);
                if (!succeeded)
                {
                    _failures++;
                }

                while (_window.Count > _killSwitch.WindowSize)
                {
                    if (!_window.Dequeue())
                    {
                        _failures--;
                    }
                }

                var tripped = _window.Count >= _killSwitch.WindowSize &&
                              (double)_failures / _window.Count >= _killSwitch.FailureRatio;
                if (tripped)
                {
                    _pausedUntil = now + _killSwitch.RestartTimeout;
                    _window.Clear();
                    _failures = 0;
                    held = _killSwitch.RestartTimeout;
                }
            }

            if (_breaker is not null)
            {
                var opened = RecordCircuit(succeeded, now);
                if (opened is { } openFor && (held is null || openFor > held))
                {
                    held = openFor;
                }
            }

            return held;
        }
    }

    /// <summary>
    /// Updates the circuit with one outcome. A closed circuit opens after the threshold of failures in a row; a half-open
    /// circuit closes on success and opens again on failure. An open circuit ignores outcomes of messages that were
    /// admitted before it opened. Returns the open duration when this outcome opened the circuit.
    /// </summary>
    private TimeSpan? RecordCircuit(bool succeeded, DateTimeOffset now)
    {
        var breaker = _breaker!;
        switch (_circuit)
        {
            case CircuitState.Closed when succeeded:
                _consecutiveFailures = 0;
                return null;
            case CircuitState.Closed:
                _consecutiveFailures++;
                return _consecutiveFailures >= breaker.FailureThreshold ? Open(now, breaker) : null;
            case CircuitState.HalfOpen when succeeded:
                _circuit = CircuitState.Closed;
                _trialInFlight = false;
                _consecutiveFailures = 0;
                return null;
            case CircuitState.HalfOpen:
                return Open(now, breaker);
            default:
                return null;
        }
    }

    private TimeSpan Open(DateTimeOffset now, CircuitBreakerOptions breaker)
    {
        _circuit = CircuitState.Open;
        _openUntil = now + breaker.OpenDuration;
        _trialInFlight = false;
        _consecutiveFailures = 0;
        return breaker.OpenDuration;
    }
}
