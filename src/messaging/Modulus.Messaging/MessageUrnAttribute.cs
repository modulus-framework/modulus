namespace Modulus.Messaging;

/// <summary>
/// Gives a message type its wire identity (RULE-10). The URN travels on the wire in place of the .NET type name, so
/// renaming the class does not change the identity of a released message. Give a new version of a message a new URN
/// (RULE-11): both versions can be consumed while producers migrate.
/// </summary>
/// <remarks>
/// A URN starts with <c>urn:</c>, has no whitespace and is at most 256 characters, for example
/// <c>urn:orders:order-submitted:v1</c>. A type without the attribute is identified by a URN derived from its full
/// name, so it still works, but renaming it changes its identity; put the attribute on every message that is released.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class MessageUrnAttribute : Attribute
{
    public const int MaxLength = 256;

    /// <summary>Creates the attribute. Throws when the URN is not a valid message URN.</summary>
    public MessageUrnAttribute(string urn)
    {
        ArgumentException.ThrowIfNullOrEmpty(urn);
        if (!urn.StartsWith("urn:", StringComparison.Ordinal) || urn.Length > MaxLength || urn.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                $"'{urn}' is not a message URN: it must start with 'urn:', contain no whitespace and be at most {MaxLength} characters.",
                nameof(urn));
        }

        Urn = urn;
    }

    /// <summary>The URN that identifies the message on the wire.</summary>
    public string Urn { get; }
}
