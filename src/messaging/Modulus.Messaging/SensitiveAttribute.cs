namespace Modulus.Messaging;

/// <summary>
/// Marks a message property whose value must never appear in logs or traces (NFR-12). Where the bus describes a message
/// for diagnostics (a parked message), the property is shown as <c>***</c>. The payload itself is not changed: consumers
/// still receive it, and encryption on the wire is a separate setting (<see cref="EncryptedMessageAttribute"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true)]
public sealed class SensitiveAttribute : Attribute
{
}
