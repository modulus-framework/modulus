using System.Text.Json;

namespace Modulus.Data.Abstractions;

/// <summary>
/// Encodes a keyset position as an opaque, URL-safe token and reads it back. The token is the key serialized as JSON,
/// base64url-encoded. A malformed token is an <see cref="ArgumentException"/>, which the HTTP layer reports as a bad request.
/// </summary>
public static class CursorCodec
{
    public static string Encode<TKey>(TKey key)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(key);
        return Convert.ToBase64String(json).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static TKey Decode<TKey>(string cursor)
    {
        ArgumentException.ThrowIfNullOrEmpty(cursor);
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            var key = JsonSerializer.Deserialize<TKey>(Convert.FromBase64String(base64));
            return key ?? throw new ArgumentException("The cursor does not hold a position.", nameof(cursor));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new ArgumentException("The cursor is not valid.", nameof(cursor), ex);
        }
    }
}
