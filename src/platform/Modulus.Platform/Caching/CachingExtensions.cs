namespace Modulus.Caching;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class CachingExtensions
{
    /// <summary>
    /// Registers the in-memory <see cref="ICacheService"/> (the dependency-free
    /// default). For a distributed cache, add the <c>Modulus.Caching.Redis</c>
    /// package and call <c>AddRedisCacheService</c>, which replaces this.
    /// </summary>
    public static IServiceCollection AddModulusCaching(
        this IServiceCollection services,
        IConfiguration configuration)
        => services.AddMemoryCacheService();

    public static IServiceCollection AddMemoryCacheService(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<ICacheService, MemoryCacheService>();
        return services;
    }
}
