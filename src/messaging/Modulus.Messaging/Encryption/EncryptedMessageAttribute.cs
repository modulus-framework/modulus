namespace Modulus.Messaging;

/// <summary>
/// Marks a message type whose body is sealed when it is sent (D6): it is not readable in the transport, the outbox or a
/// dead letter. A fault of such a message is sealed too. Needs <see cref="IMessageEncryptionKeys"/> to be registered.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class EncryptedMessageAttribute : Attribute
{
}
