namespace Modulus.Messaging;

/// <summary>
/// The body of an encrypted message on the wire (D6): AES-256-GCM ciphertext with its nonce and tag, base64. The key id
/// names the key that opens it, so the key can be rotated while older messages stay readable.
/// </summary>
public sealed record EncryptedPayload(string KeyId, string Nonce, string Tag, string Ciphertext);
