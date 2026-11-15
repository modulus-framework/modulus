namespace Modulus.Messaging;

/// <summary>
/// Delivery priority of a message type (FR-13). Higher values are taken first by transports that store priority
/// (PostgreSQL). It overrides the consumer's <see cref="ConsumerOptions.Priority"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class MessagePriorityAttribute(int priority) : Attribute
{
    public int Priority { get; } = priority;
}
