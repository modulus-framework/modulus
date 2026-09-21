using Microsoft.AspNetCore.Http;

namespace Modulus.UI.Theming.Tabler;

/// <summary>
/// Small pure helpers the shell views share (kept out of Razor so they are unit-testable). Public because an app that ejects
/// a layout or shell partial (<c>modulus ui eject Tabler</c>) compiles that view in its own assembly.
/// </summary>
public static class TablerShell
{
    /// <summary>Cookie <c>modulus.js</c> writes when the user switches color mode.</summary>
    public const string ColorModeCookie = "modulus-color-mode";

    /// <summary>
    /// Color mode to stamp on the root element: the user's cookie (only when
    /// <see cref="ThemeOptions.AllowUserThemeSwitch"/>), else the configured
    /// default. <c>system</c> renders <c>light</c>; <c>modulus.js</c> then
    /// follows <c>prefers-color-scheme</c> on the client.
    /// </summary>
    public static string ResolveColorMode(HttpRequest request, ThemeOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (options.AllowUserThemeSwitch
            && request.Cookies.TryGetValue(ColorModeCookie, out var stored)
            && Normalize(stored) is { } fromCookie)
        {
            return fromCookie == "dark" ? "dark" : "light";
        }

        return Normalize(options.ColorMode) == "dark" ? "dark" : "light";
    }

    /// <summary>True when the configured/stored mode is <c>system</c> (client resolves it).</summary>
    public static bool FollowsSystem(HttpRequest request, ThemeOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        var mode = options.AllowUserThemeSwitch
            && request.Cookies.TryGetValue(ColorModeCookie, out var stored)
            && Normalize(stored) is { } fromCookie
                ? fromCookie
                : Normalize(options.ColorMode);
        return mode == "system";
    }

    /// <summary>
    /// True when <paramref name="item"/> (or, for a group, any child) matches the
    /// current request path. Root <c>/</c> matches only exactly; other targets
    /// also match their sub-paths (<c>/users</c> is active on <c>/users/5</c>).
    /// </summary>
    public static bool IsActive(HttpRequest request, UiMenuItem item)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(item);

        return Matches(request.Path.Value, item.Url)
            || (item.Children?.Any(c => IsActive(request, c)) ?? false);
    }

    private static bool Matches(string? requestPath, string url)
    {
        var target = NormalizePath(url);
        if (target is null)
        {
            return false;
        }

        var current = NormalizePath(requestPath) ?? "/";
        if (target == "/")
        {
            return current == "/";
        }

        return current.Equals(target, StringComparison.OrdinalIgnoreCase)
            || current.StartsWith(target + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizePath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url == "#")
        {
            return null;
        }

        var path = url.StartsWith("~/", StringComparison.Ordinal) ? url[1..] : url;
        var end = path.IndexOfAny(['?', '#']);
        if (end >= 0)
        {
            path = path[..end];
        }

        if (path.Length == 0)
        {
            return null;
        }

        return path.Length > 1 ? path.TrimEnd('/') : path;
    }

    private static string? Normalize(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "light" => "light",
        "dark" => "dark",
        "system" => "system",
        _ => null,
    };
}
