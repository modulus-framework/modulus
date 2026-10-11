namespace Modulus.Core.Http;

/// <summary>
/// Checks that a return address stays on this site. Used wherever a request names where to send the browser next, so the
/// open-redirect rule is one rule everywhere.
/// </summary>
public static class LocalUrl
{
    /// <summary>
    /// True only for a path such as <c>/orders</c>. Rejects a scheme or host (<c>https://x</c>), a network path (<c>//x</c>,
    /// <c>/\x</c>), and any control character (tab, CR, LF). A browser strips a tab from <c>/%09/x</c>, so <c>/\t/x</c> would
    /// otherwise land on <c>//x</c>.
    /// </summary>
    public static bool IsLocal(string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/')
            return false;

        if (url.Length == 1)
            return true;

        if (url[1] is '/' or '\\')
            return false;

        foreach (var c in url)
        {
            if (char.IsControl(c))
                return false;
        }

        return true;
    }
}
