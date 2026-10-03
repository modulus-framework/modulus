namespace Modulus.Webhooks;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// The <see href="https://www.standardwebhooks.com/">Standard Webhooks</see> signature scheme, used both to sign
/// deliveries and, by receivers, to verify them. A secret is <c>whsec_</c> followed by base64 key bytes; the signature
/// is <c>v1,</c> + base64(HMAC-SHA256(key, "{webhook-id}.{webhook-timestamp}.{body}")), and the
/// <c>webhook-signature</c> header lists one or more of them separated by spaces (several during a secret rotation).
/// </summary>
public static class StandardWebhooks
{
    /// <summary>The message id header.</summary>
    public const string IdHeader = "webhook-id";

    /// <summary>The Unix-seconds timestamp header.</summary>
    public const string TimestampHeader = "webhook-timestamp";

    /// <summary>The signature header.</summary>
    public const string SignatureHeader = "webhook-signature";

    /// <summary>The secret prefix.</summary>
    public const string SecretPrefix = "whsec_";

    private const string SignatureVersion = "v1";

    /// <summary>Creates a secret: <c>whsec_</c> + 32 random bytes in base64.</summary>
    public static string GenerateSecret()
        => SecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>Whether <paramref name="secret"/> is <c>whsec_</c> + base64 of 24 to 64 bytes (the spec's range).</summary>
    public static bool IsValidSecret(string? secret)
        => TryDecodeSecret(secret, out var key) && key.Length is >= 24 and <= 64;

    /// <summary>The <c>v1,</c> signature of a message.</summary>
    public static string Sign(string secret, string messageId, DateTimeOffset timestamp, string body)
    {
        ArgumentNullException.ThrowIfNull(messageId);
        ArgumentNullException.ThrowIfNull(body);
        if (!TryDecodeSecret(secret, out var key))
            throw new ArgumentException("The secret is not a valid 'whsec_' base64 secret.", nameof(secret));

        var content = Encoding.UTF8.GetBytes(SignedContent(messageId, timestamp.ToUnixTimeSeconds(), body));
        return SignatureVersion + "," + Convert.ToBase64String(HMACSHA256.HashData(key, content));
    }

    /// <summary>
    /// Verifies a received message against any of <paramref name="secrets"/>: the timestamp must be within
    /// <paramref name="tolerance"/> of <paramref name="now"/> (default 5 minutes, against replays) and one of the header's
    /// <c>v1</c> signatures must match (compared in constant time).
    /// </summary>
    public static WebhookVerificationResult Verify(
        IEnumerable<string> secrets,
        string? messageId,
        string? timestamp,
        string? signatureHeader,
        string body,
        DateTimeOffset now,
        TimeSpan? tolerance = null)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrEmpty(messageId) || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signatureHeader))
            return WebhookVerificationResult.MissingHeaders;

        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            return WebhookVerificationResult.InvalidTimestamp;
        var window = tolerance ?? TimeSpan.FromMinutes(5);
        var sent = DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(seconds, 0, 253402300799));
        if ((now - sent).Duration() > window)
            return WebhookVerificationResult.InvalidTimestamp;

        var content = Encoding.UTF8.GetBytes(SignedContent(messageId, seconds, body));
        var expected = secrets
            .Select(s => TryDecodeSecret(s, out var key) ? HMACSHA256.HashData(key, content) : null)
            .OfType<byte[]>()
            .ToList();

        foreach (var part in signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var comma = part.IndexOf(',', StringComparison.Ordinal);
            if (comma < 0 || !string.Equals(part[..comma], SignatureVersion, StringComparison.Ordinal))
                continue;

            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(part[(comma + 1)..]);
            }
            catch (FormatException)
            {
                continue;
            }

            if (expected.Any(e => CryptographicOperations.FixedTimeEquals(e, signature)))
                return WebhookVerificationResult.Valid;
        }

        return WebhookVerificationResult.InvalidSignature;
    }

    private static string SignedContent(string messageId, long timestamp, string body)
        => string.Create(CultureInfo.InvariantCulture, $"{messageId}.{timestamp}.{body}");

    private static bool TryDecodeSecret(string? secret, out byte[] key)
    {
        key = [];
        if (string.IsNullOrEmpty(secret))
            return false;

        var encoded = secret.StartsWith(SecretPrefix, StringComparison.Ordinal) ? secret[SecretPrefix.Length..] : secret;
        try
        {
            key = Convert.FromBase64String(encoded);
            return key.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>The outcome of <see cref="StandardWebhooks.Verify"/>.</summary>
public enum WebhookVerificationResult
{
    /// <summary>A signature matched and the timestamp is recent.</summary>
    Valid,

    /// <summary>A <c>webhook-*</c> header is missing.</summary>
    MissingHeaders,

    /// <summary>The timestamp is not a number or is outside the tolerance (a possible replay).</summary>
    InvalidTimestamp,

    /// <summary>No signature matched.</summary>
    InvalidSignature,
}
