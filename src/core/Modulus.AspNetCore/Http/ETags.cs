namespace Modulus.AspNetCore.Http;

using Microsoft.Extensions.Primitives;

/// <summary>Entity-tag helpers for conditional requests (RFC 9110 §8.8.3, §13).</summary>
public static class ETags
{
    /// <summary>Formats <paramref name="value"/> as a strong entity tag: <c>"value"</c>.</summary>
    public static string Format(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value : $"\"{value}\"";
    }

    /// <summary>
    /// True when an <c>If-None-Match</c> header means the caller already has <paramref name="etag"/>: it lists the tag
    /// (weak comparison, so <c>W/"x"</c> matches <c>"x"</c>) or is <c>*</c>.
    /// </summary>
    public static bool NoneMatchSatisfied(StringValues ifNoneMatch, string etag)
        => Any(ifNoneMatch, etag, weak: true);

    /// <summary>
    /// True when an <c>If-Match</c> header allows the request to proceed against the current <paramref name="etag"/>:
    /// it lists the tag (strong comparison: a weak tag never matches) or is <c>*</c>. A missing header is not checked here.
    /// </summary>
    public static bool IfMatchSatisfied(StringValues ifMatch, string etag)
        => Any(ifMatch, etag, weak: false);

    private static bool Any(StringValues header, string etag, bool weak)
    {
        var current = Format(etag);
        foreach (var line in header)
        {
            if (line is null)
                continue;

            foreach (var part in line.Split(','))
            {
                var candidate = part.Trim();
                if (candidate == "*")
                    return true;

                var isWeak = candidate.StartsWith("W/", StringComparison.Ordinal);
                if (isWeak && !weak)
                    continue;

                if (string.Equals(isWeak ? candidate[2..] : candidate, current, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }
}
