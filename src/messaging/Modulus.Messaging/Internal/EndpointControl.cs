namespace Modulus.Messaging.Internal;

/// <summary>
/// Admission control for one endpoint: the rate limit and the kill switch. Every message waits here before its
/// first attempt, and each final outcome is recorded here so the kill switch can trip.
/// </summary>
internal sealed class EndpointControl
{
    private readonly RateLimit? _rate;
    private readonly KillSwitchOptions? _killSwitch;
    private readonly Queue<bool> _window = new();
    private readonly Queue<DateTimeOffset> _admissions = new();
    private readonly object _gate = new();
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;
    private int _failures;

    internal EndpointControl(ConsumerOptions options)
    {
        _rate = options.RateLimit;
        _killSwitch = options.KillSwitch?.Clone();
    }

    /// <summary>Waits out a kill-switch pause, then waits for a rate-limit slot.</summary>
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
                else if (_rate is not null)
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
                else
                {
                    return;
                }
            }

            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Records the final outcome of one message and pauses the endpoint if the kill switch trips. Returns the pause
    /// when this outcome tripped it, otherwise null.
    /// </summary>
    internal TimeSpan? Record(bool succeeded)
    {
        if (_killSwitch is null)
        {
            return null;
        }

        lock (_gate)
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
            if (!tripped)
            {
                return null;
            }

            _pausedUntil = DateTimeOffset.UtcNow + _killSwitch.RestartTimeout;
            _window.Clear();
            _failures = 0;
            return _killSwitch.RestartTimeout;
        }
    }
}
