namespace Modulus.Messaging;

/// <summary>
/// Time-to-live of a message type (FR-13). A message not processed within it is moved to <c>_skipped</c> unprocessed.
/// It overrides the consumer's <see cref="ConsumerOptions.TimeToLive"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class MessageTimeToLiveAttribute(int milliseconds) : Attribute
{
    /// <summary>The time-to-live in milliseconds.</summary>
    public int Milliseconds { get; } = milliseconds > 0
        ? milliseconds
        : throw new ArgumentOutOfRangeException(nameof(milliseconds), "Must be positive.");

    /// <summary>The time-to-live as a span.</summary>
    public TimeSpan TimeToLive => TimeSpan.FromMilliseconds(Milliseconds);
}
