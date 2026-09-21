using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Modulus.UI.Theming.Tabler;

/// <summary>DI wiring for the Tabler theme.</summary>
public static class TablerThemeExtensions
{
    /// <summary>
    /// Registers <see cref="TablerTheme"/> (via <c>AddModulusTheme</c>) and the
    /// <see cref="TablerThemeModule"/> manifest. Pass <paramref name="configuration"/>
    /// to bind <c>Modulus:Ui</c> (branding/features) and <c>Modulus:Ui:Theme</c>.
    /// Safe to call multiple times.
    /// </summary>
    public static IServiceCollection AddTablerTheme(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configuration is null)
        {
            services.AddModulusTheme<TablerTheme>();
        }
        else
        {
            services.AddModulusTheme<TablerTheme>(configuration);
        }

        services.AddUiModule<TablerThemeModule>();
        return services;
    }
}
