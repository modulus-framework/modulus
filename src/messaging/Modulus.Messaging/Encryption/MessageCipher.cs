using System.Security.Cryptography;
using System.Text;

namespace Modulus.Messaging.Internal;

/// <summary>AES-256-GCM over a message body. The associated data binds the body to its message id, endpoint, consumer and type.</summary>
internal static class MessageCipher
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    internal static EncryptedPayload Encrypt(string keyId, byte[] key, byte[] plaintext, byte[] associatedData)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        return new EncryptedPayload(keyId, Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToBase64String(ciphertext));
    }

    /// <summary>Opens a payload. Throws <see cref="CryptographicException"/> when the key, the data or the associated data is wrong.</summary>
    internal static byte[] Decrypt(byte[] key, EncryptedPayload payload, byte[] associatedData)
    {
        var nonce = Convert.FromBase64String(payload.Nonce);
        var tag = Convert.FromBase64String(payload.Tag);
        var ciphertext = Convert.FromBase64String(payload.Ciphertext);
        if (nonce.Length != NonceSize || tag.Length != TagSize)
        {
            throw new CryptographicException("The encrypted payload has a malformed nonce or tag.");
        }

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        return plaintext;
    }

    /// <summary>
    /// The message id, endpoint, consumer key and type of the envelope. A body moved to another message, endpoint or
    /// consumer, or read as another type, fails to open.
    /// </summary>
    internal static byte[] AssociatedData(MessageEnvelope envelope, string typeName) =>
        Encoding.UTF8.GetBytes(string.Join('|', envelope.MessageId.ToString("N"), envelope.Endpoint, envelope.ConsumerKey ?? string.Empty, typeName));
}
