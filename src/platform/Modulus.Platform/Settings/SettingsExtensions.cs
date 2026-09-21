namespace Modulus.Settings;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.TryAddSingleton<ISettingDefinitionRegistry, SettingDefinitionRegistry>();
        services.TryAddSingleton<ISettingStore, InMemorySettingStore>();
        services.TryAddScoped<ISettingManager, SettingManager>();
        return services;
    }
}
