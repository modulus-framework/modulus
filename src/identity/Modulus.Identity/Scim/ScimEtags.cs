namespace Modulus.Identity.Scim;

/// <summary>
/// SCIM entity tags (RFC 7644 §3.14). A representation's tag is derived from the account's or group's concurrency stamp,
/// which Identity changes on every update. A change sent with <c>If-Match</c> is applied only when the tag still matches.
/// </summary>
internal static class ScimEtags
{
    /// <summary>The weak tag for a concurrency stamp, or null when the record has none.</summary>
    public static string? Of(string? concurrencyStamp)
        => string.IsNullOrEmpty(concurrencyStamp) ? null : $"W/\"{concurrencyStamp}\"";

    /// <summary>
    /// Whether an <c>If-Match</c> value matches the current tag. <c>*</c> matches any existing resource; a list matches when
    /// one of its tags does. Tags compare on their opaque value, so the weak prefix does not matter (RFC 9110 §13.1.2).
    /// </summary>
    public static bool Matches(string ifMatch, string? current)
    {
        if (current is null)
            return false;
        if (ifMatch.Trim() == "*")
            return true;

        var expected = Opaque(current);
        return ifMatch.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(tag => Opaque(tag) == expected);
    }

    private static string Opaque(string tag)
    {
        var value = tag.StartsWith("W/", StringComparison.Ordinal) ? tag[2..] : tag;
        return value.Trim('"');
    }
}
