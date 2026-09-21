using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.UI.Theming;

namespace Modulus.UI;

/// <summary>Read-only accessor for the active <see cref="ITheme"/>.</summary>
public interface IThemeAccessor
{
    /// <summary>The theme whose <see cref="ITheme.Name"/> matches <see cref="ThemeOptions.Name"/>.</summary>
    ITheme Current { get; }
}

/// <summary>
/// Default <see cref="IThemeAccessor"/>: resolves the active theme once from
/// all registered <see cref="ITheme"/>s and fails fast at first use when
/// <see cref="ThemeOptions.Name"/> matches nothing (e.g. the theme package was
/// not referenced) or multiple themes share a name (first registration wins).
/// </summary>
public sealed class ThemeAccessor : IThemeAccessor
{
    private readonly ITheme _theme;

    public ThemeAccessor(IServiceProvider serviceProvider, IOptions<ThemeOptions> options)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(options);

        var registered = serviceProvider.GetServices<ITheme>().ToList();
        var name = options.Value.Name;
        var theme = registered.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        if (theme is null)
        {
            var available = registered.Count > 0
                ? string.Join(", ", registered.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase))
                : "<none — reference a theme package and call AddTablerTheme()>";
            throw new InvalidOperationException(
                $"No ITheme named '{name}' is registered (Modulus:Ui:Theme:Name). Available: {available}.");
        }

        _theme = theme;
    }

    /// <inheritdoc />
    public ITheme Current => _theme;
}
