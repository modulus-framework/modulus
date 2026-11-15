namespace Modulus.Messaging;

/// <summary>
/// Retries within one delivery. Each retry runs in a new DI scope (RULE-07). Exceptions listed with
/// <see cref="Ignore{TException}"/> skip retries and go straight to <c>_error</c>.
/// </summary>
public sealed class RetryPolicy
{
    private readonly Func<int, TimeSpan> _delayFor;
    private readonly Type[] _ignored;

    private RetryPolicy(int retries, Func<int, TimeSpan> delayFor, Type[] ignored)
    {
        Retries = retries;
        _delayFor = delayFor;
        _ignored = ignored;
    }

    /// <summary>Number of retries after the first attempt.</summary>
    public int Retries { get; }

    /// <summary>Delay before retry <paramref name="retryNumber"/> (1 for the first retry).</summary>
    public TimeSpan DelayFor(int retryNumber) => _delayFor(retryNumber);

    /// <summary>True when the exception skips retries.</summary>
    public bool IsIgnored(Exception exception) => Array.Exists(_ignored, t => t.IsInstanceOfType(exception));

    /// <summary>Returns a copy that does not retry <typeparamref name="TException"/>.</summary>
    public RetryPolicy Ignore<TException>() where TException : Exception =>
        new(Retries, _delayFor, [.. _ignored, typeof(TException)]);

    public static RetryPolicy Immediate(int retries) =>
        new(RequireNonNegative(retries), _ => TimeSpan.Zero, []);

    public static RetryPolicy Interval(int retries, TimeSpan interval)
    {
        RequireNonNegative(interval);
        return new(RequireNonNegative(retries), _ => interval, []);
    }

    /// <summary>One retry per interval; the number of retries is the number of intervals.</summary>
    public static RetryPolicy Intervals(params TimeSpan[] intervals)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        if (intervals.Length == 0)
        {
            throw new ArgumentException("At least one interval is required.", nameof(intervals));
        }

        foreach (var interval in intervals)
        {
            RequireNonNegative(interval);
        }

        var copy = (TimeSpan[])intervals.Clone();
        return new(copy.Length, n => copy[n - 1], []);
    }

    public static RetryPolicy Incremental(int retries, TimeSpan initial, TimeSpan increment)
    {
        RequireNonNegative(initial);
        RequireNonNegative(increment);
        return new(RequireNonNegative(retries), n => initial + TimeSpan.FromTicks(increment.Ticks * (n - 1)), []);
    }

    /// <summary>Doubles the delay each retry, from <paramref name="minimum"/> up to <paramref name="maximum"/>.</summary>
    public static RetryPolicy Exponential(int retries, TimeSpan minimum, TimeSpan maximum)
    {
        RequireNonNegative(minimum);
        RequireNonNegative(maximum);
        return new(RequireNonNegative(retries), n =>
        {
            var ticks = minimum.Ticks * Math.Pow(2, n - 1);
            return ticks >= maximum.Ticks ? maximum : TimeSpan.FromTicks((long)ticks);
        }, []);
    }

    private static int RequireNonNegative(int value) =>
        value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Must not be negative.") : value;

    private static TimeSpan RequireNonNegative(TimeSpan value) =>
        value < TimeSpan.Zero ? throw new ArgumentOutOfRangeException(nameof(value), "Must not be negative.") : value;
}
