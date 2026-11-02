namespace Modulus.Media;

using System.Text;

/// <summary>
/// Turns a client's file name into a display name: directory parts and control characters are dropped and the length is capped. The result
/// is only ever shown; storage keys come from the media id.
/// </summary>
public static class MediaFileName
{
    private const string Fallback = "file";

    public static string Sanitize(string? name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Fallback;

        // Keep only the last segment of either separator style, so "..\..\x.pdf" becomes "x.pdf".
        var leaf = name.Split('/', '\\').Last();

        var builder = new StringBuilder(leaf.Length);
        foreach (var ch in leaf)
        {
            if (!char.IsControl(ch))
                builder.Append(ch);
        }

        var cleaned = builder.ToString().Trim();
        if (cleaned.Length == 0 || cleaned is "." or "..")
            return Fallback;

        return cleaned.Length > maxLength ? cleaned[..maxLength] : cleaned;
    }
}
