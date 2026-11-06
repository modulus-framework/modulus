namespace Modulus.Analytics;

using System.Security.Cryptography;
using System.Text;

/// <summary>Stable record ids, so the same row always becomes the same analytics record.</summary>
public static class AnalyticsRecordIds
{
    /// <summary>
    /// Derives a GUID from <paramref name="scope"/> and <paramref name="key"/> (a SHA-256 hash, shaped as a version 8 UUID). The same
    /// input always gives the same id, on every machine, so a re-run of a backfill collapses onto the earlier rows.
    /// </summary>
    /// <param name="scope">Separates id spaces, for example the dataset name.</param>
    /// <param name="key">The row's key, in a stable text form.</param>
    public static Guid Deterministic(string scope, string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(scope);
        ArgumentNullException.ThrowIfNull(key);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(scope + "\u001f" + key));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
