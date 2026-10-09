namespace Modulus;

/// <summary>Unpadded URL-safe Base64 (RFC 4648 §5); <c>System.Buffers.Text.Base64Url</c> only exists from .NET 9.</summary>
internal static class Base64UrlCompat
{
    public static string Encode(ReadOnlySpan<byte> data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            1 => throw new FormatException("Invalid Base64Url length."),
            _ => padded,
        };
        return Convert.FromBase64String(padded);
    }
}
