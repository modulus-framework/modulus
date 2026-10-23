namespace Modulus.MultiTenancy.EntityFrameworkCore;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modulus.Settings;

public static class EfSettingStoreExtensions
{
    /// <summary>
    /// Stores setting values in the tenant store database (<c>ModulusSettingValues</c>) instead of memory. Requires
    /// <c>AddEfCoreTenantStore</c> (the context) and <c>AddModulusSettings</c>. Reads are cached on this node for
    /// <c>Settings:CacheDuration</c> (30 seconds by default; zero turns the cache off), and a write here makes the cache stale at once.
    /// The table ships no migration: the host adds one to its tenant store context, or applies the upgrade script for an existing database.
    /// </summary>
    public static IServiceCollection AddEfCoreSettingStore(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddModulusSettings();
        services.TryAddSingleton<ISettingChangeNotifier, NullSettingChangeNotifier>();
        // Without Redis, the other nodes learn of a change from the database counter (see SettingVersionPoller).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SettingVersionPoller>());
        services.RemoveAll<ISettingStore>();
        services.AddScoped<EfSettingStore>();
        services.AddScoped<ISettingStore>(sp =>
        {
            var store = sp.GetRequiredService<EfSettingStore>();
            var duration = sp.GetRequiredService<IOptions<ModulusSettingsOptions>>().Value.CacheDuration;
            return duration > TimeSpan.Zero
                ? new CachedSettingStore(
                    store,
                    sp.GetRequiredService<IMemoryCache>(),
                    sp.GetRequiredService<SettingCacheVersion>(),
                    duration,
                    sp.GetRequiredService<ISettingChangeNotifier>())
                : store;
        });
        return services;
    }
}
