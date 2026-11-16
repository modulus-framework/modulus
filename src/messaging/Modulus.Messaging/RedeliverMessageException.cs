namespace Modulus.Messaging;

/// <summary>
/// Ends a consume attempt on purpose and brings the message back after <see cref="Delay"/> (FR-08). Raised by
/// <see cref="ConsumeContext{TMessage}.Redeliver"/>. It is not a fault: the attempt's work is rolled back with its unit
/// of work, the retry policy does not count it, and it does not feed the kill switch. A consumer that catches it by
/// accident (a broad <c>catch (Exception)</c>) turns the request into an ordinary failure, so let it propagate.
/// </summary>
public sealed class RedeliverMessageException : Exception
{
    /// <summary>Creates the request. A negative delay is refused.</summary>
    public RedeliverMessageException(TimeSpan delay)
        : base($"The consumer asked for the message to be redelivered in {delay}.")
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), "A redelivery delay must not be negative.");
        }

        Delay = delay;
    }

    /// <summary>How long the message waits before it is delivered again.</summary>
    public TimeSpan Delay { get; }
}
