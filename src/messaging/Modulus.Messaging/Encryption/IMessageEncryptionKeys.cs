namespace Modulus.Messaging;

/// <summary>
/// The keys that seal and open message bodies (D6). Register one in the service collection before the bus to enable
/// encryption; a message type marked <see cref="EncryptedMessageAttribute"/>, or an endpoint with
/// <see cref="ConsumerOptions.EncryptPayload"/>, is then sealed with <see cref="CurrentKeyId"/>.
/// </summary>
/// <remarks>
/// Keys are secrets: keep them in a secret store or user secrets, never in source. Rotate by making a new key the current
/// one and keeping the old ones available to open messages sealed before the change.
/// </remarks>
public interface IMessageEncryptionKeys
{
    /// <summary>The key id new messages are sealed with.</summary>
    string CurrentKeyId { get; }

    /// <summary>The 32-byte key for an id, or null when this host does not have it.</summary>
    byte[]? GetKey(string keyId);
}
