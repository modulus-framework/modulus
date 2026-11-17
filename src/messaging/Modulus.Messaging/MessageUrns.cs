namespace Modulus.Messaging;

/// <summary>Public access to the wire identity of message types, for contract checks and tooling (RULE-10, RULE-11).</summary>
public static class MessageUrns
{
    /// <summary>The URN a message type is sent under: its <see cref="MessageUrnAttribute"/>, or the URN derived from its full name.</summary>
    public static string Of(Type messageType) => MessageUrn.Of(messageType);
}
