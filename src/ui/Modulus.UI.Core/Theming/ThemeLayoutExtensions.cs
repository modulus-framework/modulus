using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.UI.Theming;

namespace Modulus.UI;

/// <summary>
/// Layout selection for UI pages. Feature UIs (Identity, Users, ...) call
/// <see cref="GetThemeLayout"/> from their <c>_ViewStart.cshtml</c> so they render
/// through whichever <see cref="ITheme"/> the host registered, without referencing
/// a concrete theme:
/// <code>Layout = Context.GetThemeLayout(StandardLayouts.Application);</code>
/// </summary>
public static class ThemeLayoutExtensions
{
    /// <summary>
    /// Shell used when the host registered no <see cref="ITheme"/> (apps generated before the
    /// theme packages existed, which only call <c>AddModulusUi()</c>). Keeps them rendering.
    /// </summary>
    public const string LegacyLayout = "_UiLayout";

    /// <summary>
    /// Resolves the layout view path for <paramref name="layoutName"/> (a
    /// <see cref="StandardLayouts"/> name, remapped through <see cref="ThemeOptions.Layouts"/>)
    /// on the active theme. Returns <c>null</c> for htmx fragment requests so swapped
    /// content never nests a second shell, and <see cref="LegacyLayout"/> when no theme is
    /// registered.
    /// </summary>
    public static string? GetThemeLayout(this HttpContext http, string layoutName = StandardLayouts.Application)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutName);

        if (http.Request.IsHtmxFragment())
        {
            return null;
        }

        var services = http.RequestServices;
        if (!services.GetServices<ITheme>().Any())
        {
            return LegacyLayout;
        }

        var theme = services.GetRequiredService<IThemeAccessor>().Current;
        var options = services.GetRequiredService<IOptions<ThemeOptions>>().Value;
        return theme.GetLayout(options.ResolveLayout(layoutName));
    }
}
