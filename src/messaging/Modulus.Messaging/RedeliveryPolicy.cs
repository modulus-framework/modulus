namespace Modulus.Messaging;

/// <summary>
/// Delayed redeliveries after retries are exhausted. A message is redelivered once per delay, with its
/// redelivery count incremented, and parked in <c>_error</c> after the last one (FR-08).
/// </summary>
public sealed class RedeliveryPolicy
{
    private RedeliveryPolicy(TimeSpan[] delays) => Delays = delays;

    public IReadOnlyList<TimeSpan> Delays { get; }

    public static RedeliveryPolicy Intervals(params TimeSpan[] delays)
    {
        ArgumentNullException.ThrowIfNull(delays);
        if (delays.Length == 0)
        {
            throw new ArgumentException("At least one delay is required.", nameof(delays));
        }

        foreach (var delay in delays)
        {
            if (delay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delays), "Delays must not be negative.");
            }
        }

        return new RedeliveryPolicy((TimeSpan[])delays.Clone());
    }
}
