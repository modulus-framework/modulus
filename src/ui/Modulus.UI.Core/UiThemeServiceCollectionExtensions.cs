using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.UI.Theming;

namespace Modulus.UI;

/// <summary>
/// DI wiring for theming and shell contributors. Call
/// <see cref="AddModulusTheme{TTheme}(IServiceCollection)"/> once (the Tabler package's
/// <c>AddTablerTheme()</c> wraps it), then
/// <c>AddMenuContributor&lt;T&gt;()</c> / <c>AddToolbarContributor&lt;T&gt;()</c> /
/// <c>AddSlotContributor&lt;T&gt;()</c> per contributor.
/// </summary>
public static class UiThemeServiceCollectionExtensions
{
    /// <summary>
    /// Registers a theme implementation plus the theme seams:
    /// <see cref="IThemeAccessor"/>, <see cref="IModulusViewResolver"/>
    /// (with <c>IMemoryCache</c>), and identity seeding of
    /// <see cref="ThemeOptions.Layouts"/>. Safe to call multiple times.
    /// </summary>
    public static IServiceCollection AddModulusTheme<TTheme>(this IServiceCollection services)
        where TTheme : class, ITheme
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddModulusUi();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITheme, TTheme>());
        services.TryAddSingleton<IThemeAccessor, ThemeAccessor>();
        services.TryAddSingleton<IModulusViewResolver, ModulusViewResolver>();
        services.AddMemoryCache();
        services.AddOptions<UiOptions>();
        services.AddOptions<ThemeOptions>().PostConfigure(static options =>
        {
            foreach (var layout in StandardLayouts.All)
            {
                options.Layouts.TryAdd(layout, layout);
            }
        });
        return services;
    }

    /// <summary>
    /// Registers a theme and binds <see cref="UiOptions"/> (<c>Modulus:Ui</c>)
    /// and <see cref="ThemeOptions"/> (<c>Modulus:Ui:Theme</c>) from configuration.
    /// </summary>
    public static IServiceCollection AddModulusTheme<TTheme>(this IServiceCollection services, IConfiguration configuration)
        where TTheme : class, ITheme
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddModulusTheme<TTheme>();
        services.Configure<UiOptions>(configuration.GetSection(UiOptions.SectionName));
        services.Configure<ThemeOptions>(configuration.GetSection(ThemeOptions.SectionName));
        return services;
    }

    /// <summary>Registers a stateless navigation contributor (singleton). Idempotent per type.</summary>
    public static IServiceCollection AddMenuContributor<TContributor>(this IServiceCollection services)
        where TContributor : class, IMenuContributor
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddModulusUi();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMenuContributor, TContributor>());
        return services;
    }

    /// <summary>Registers a scoped toolbar contributor. Idempotent per type.</summary>
    public static IServiceCollection AddToolbarContributor<TContributor>(this IServiceCollection services)
        where TContributor : class, IToolbarContributor
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddModulusUi();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IToolbarContributor, TContributor>());
        return services;
    }

    /// <summary>Registers a scoped breadcrumb contributor. Idempotent per type.</summary>
    public static IServiceCollection AddBreadcrumbContributor<TContributor>(this IServiceCollection services)
        where TContributor : class, IBreadcrumbContributor
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddModulusUi();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IBreadcrumbContributor, TContributor>());
        return services;
    }

    /// <summary>Registers a scoped slot contributor. Idempotent per type.</summary>
    public static IServiceCollection AddSlotContributor<TContributor>(this IServiceCollection services)
        where TContributor : class, ISlotContributor
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddModulusUi();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISlotContributor, TContributor>());
        return services;
    }
}
