namespace Modulus.Data.Redis.Extensions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Modulus.Data.Abstractions;

public static class RedisServiceCollectionExtensions
{
    public static IServiceCollection AddRedisStore(
        this IServiceCollection services,
        Action<RedisOptions> configure)
    {
        var opts = new RedisOptions();
        configure(opts);
        services.AddSingleton(Options.Create(opts));

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(opts.ConnectionString));

        services.AddScoped<ICacheRepository, RedisCacheRepository>();
        services.AddScoped<RedisGeoService>();
        services.AddScoped<RedisPubSubService>();

        return services;
    }
}