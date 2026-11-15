namespace Modulus.Messaging;

/// <summary>
/// Keys held in memory, given as base64 strings of 32 bytes each (AES-256). Bind them from configuration or a secret store;
/// the strings are secrets.
/// </summary>
public sealed class StaticMessageEncryptionKeys : IMessageEncryptionKeys
{
    private readonly Dictionary<string, byte[]> _keys = new(StringComparer.Ordinal);

    /// <summary>Creates the key set.</summary>
    /// <param name="currentKeyId">The id new messages are sealed with. It must be one of the keys.</param>
    /// <param name="base64Keys">Key id to base64 key.</param>
    public StaticMessageEncryptionKeys(string currentKeyId, IReadOnlyDictionary<string, string> base64Keys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentKeyId);
        ArgumentNullException.ThrowIfNull(base64Keys);
        foreach (var (keyId, text) in base64Keys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
            var key = Convert.FromBase64String(text);
            if (key.Length != 32)
            {
                throw new ArgumentException($"Key '{keyId}' must be 32 bytes (AES-256), not {key.Length}.", nameof(base64Keys));
            }

            _keys[keyId] = key;
        }

        if (!_keys.ContainsKey(currentKeyId))
        {
            throw new ArgumentException($"The current key '{currentKeyId}' is not among the keys.", nameof(currentKeyId));
        }

        CurrentKeyId = currentKeyId;
    }

    public string CurrentKeyId { get; }

    /// <inheritdoc/>
    public byte[]? GetKey(string keyId) =>
        keyId is not null && _keys.TryGetValue(keyId, out var key) ? (byte[])key.Clone() : null;
}
