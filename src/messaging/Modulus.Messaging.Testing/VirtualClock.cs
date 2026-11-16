namespace Modulus.Messaging.Testing;

/// <summary>
/// A clock that moves only when a test advances it (FR-25). Timers (scheduled messages, redelivery delays, sagas' timeouts)
/// fire in due-time order during <see cref="Advance"/>, so a timeout of minutes passes in microseconds. Pass it to
/// <see cref="MessagingTestHarness.StartAsync(Action{MessagingBusBuilder}, Action{IServiceCollection}?, VirtualClock)"/>.
/// </summary>
public sealed class VirtualClock : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<VirtualTimer> _timers = [];
    private DateTimeOffset _now;

    public VirtualClock()
        : this(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
    {
    }

    public VirtualClock(DateTimeOffset start)
    {
        _now = start;
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new VirtualTimer(this, callback, state, period);
        lock (_gate)
        {
            timer.Due = _now + dueTime;
            _timers.Add(timer);
        }

        return timer;
    }

    /// <summary>Moves the clock forward, firing every timer that comes due on the way, in order.</summary>
    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(by), "The clock does not go backwards.");
        }

        DateTimeOffset target;
        lock (_gate)
        {
            target = _now + by;
        }

        while (true)
        {
            VirtualTimer? due = null;
            lock (_gate)
            {
                due = _timers
                    .Where(t => t.IsActive && t.Due <= target)
                    .OrderBy(t => t.Due)
                    .FirstOrDefault();
                if (due is null)
                {
                    _now = target;
                    return;
                }

                _now = due.Due;
            }

            due.Fire();
        }
    }

    private void Remove(VirtualTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class VirtualTimer(VirtualClock clock, TimerCallback callback, object? state, TimeSpan period) : ITimer
    {
        private bool _disposed;

        internal DateTimeOffset Due { get; set; }

        internal bool IsActive => !_disposed;

        internal void Fire()
        {
            callback(state);
            lock (clock._gate)
            {
                if (_disposed)
                {
                    return;
                }

                if (period == Timeout.InfiniteTimeSpan)
                {
                    _disposed = true;
                    clock._timers.Remove(this);
                }
                else
                {
                    Due += period;
                }
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan periodTime)
        {
            lock (clock._gate)
            {
                if (_disposed)
                {
                    return false;
                }

                Due = clock._now + dueTime;
                return true;
            }
        }

        public void Dispose()
        {
            _disposed = true;
            clock.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
