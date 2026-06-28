namespace Modulus.Caching;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

public static class CachingExtensions
{
    public static IServiceCollection AddModulusCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Caching:Provider"]?.ToLowerInvariant();
        switch (provider)
        {
            case "redis":
                var connString = configuration["Caching:Redis:ConnectionString"]
                    ?? throw new InvalidOperationException("Caching:Redis:ConnectionString is required.");
                return services.AddRedisCacheService(connString);
            default:
                return services.AddMemoryCacheService();
        }
    }

    public static IServiceCollection AddMemoryCacheService(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<ICacheService, MemoryCacheService>();
        return services;
    }

    public static IServiceCollection AddRedisCacheService(
        this IServiceCollection services,
        string connectionString)
    {
        var multiplexer = ConnectionMultiplexer.Connect(connectionString);
        services.AddSingleton<IConnectionMultiplexer>(multiplexer);
        services.AddSingleton<ICacheService, RedisCacheService>();
        return services;
    }
}
