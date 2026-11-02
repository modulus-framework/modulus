namespace Modulus.Settings;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

public static class SettingsExtensions
{
    /// <summary>
    /// Registers the setting system: definition registry (singleton),
    /// in-memory store (singleton, single-node default), and scoped manager.
    /// Register a custom <see cref="ISettingStore"/> <i>before</i> this call
    /// to use a distributed store — <c>TryAdd</c> keeps yours.
    /// </summary>
    public static IServiceCollection AddModulusSettings(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<ModulusSettingsOptions>();
        services.TryAddSingleton<ISettingDefinitionRegistry>(provider =>
        {
            // Contributors run once, when the registry is first resolved, in registration order.
            var registry = new SettingDefinitionRegistry();
            foreach (var contributor in provider.GetServices<ISettingDefinitionContributor>())
                contributor.Contribute(registry);
            return registry;
        });
        services.TryAddSingleton<ISettingStore, InMemorySettingStore>();
        services.TryAddSingleton<SettingCacheVersion>();
        services.TryAddScoped<ISettingManager, SettingManager>();
        services.TryAddScoped<ISettingAdministration, SettingAdministration>();

        // The Settings section (pins, cache duration) is read from the host's configuration when there is one, so a host that
        // registers the setting system without passing its configuration still honours the pins.
        services.AddOptions<ModulusSettingsOptions>()
            .Configure<IServiceProvider>((options, provider) => provider.GetService<IConfiguration>()?.GetSection("Settings").Bind(options));
        return services;
    }

    /// <summary>
    /// Registers the application identity, branding and regional setting definitions (<see cref="ApplicationSettingsCatalog"/>).
    /// Call after <see cref="AddModulusSettings(IServiceCollection)"/>.
    /// </summary>
    public static IServiceCollection AddModulusApplicationSettings(this IServiceCollection services)
    {
        services.AddModulusSettings();
        services.AddSingleton<ISettingDefinitionContributor, ApplicationSettingsCatalog>();
        return services;
    }

    /// <summary>
    /// <see cref="AddModulusSettings(IServiceCollection)"/> plus the <c>Settings</c> configuration section (pins and cache duration).
    /// </summary>
    public static IServiceCollection AddModulusSettings(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddModulusSettings();
        services.Configure<ModulusSettingsOptions>(configuration.GetSection("Settings"));
        return services;
    }
}
