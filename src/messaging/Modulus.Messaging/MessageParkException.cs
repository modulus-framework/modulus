namespace Modulus.Messaging;

/// <summary>
/// Thrown from a consume scope or a consumer to park the message at once: no retry and no redelivery, the message goes
/// to <c>_error</c> with <see cref="Reason"/> recorded (FR-09). Use it for a failure that retrying cannot fix, such as a
/// company that is unknown or inactive (D8). A transient failure must not use it: it is retried as usual.
/// </summary>
public sealed class MessageParkException : Exception
{
    public MessageParkException(string reason, string message, Exception? inner = null)
        : base(message, inner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <summary>The short reason recorded with the parked message, for example <c>tenant-refused</c>.</summary>
    public string Reason { get; }
}
