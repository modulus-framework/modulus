namespace Modulus.SignalR.Redis.Extensions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class RedisBackplaneExtensions
{
    /// <summary>
    /// Configures SignalR with a Redis backplane for horizontal scaling.
    /// Reads connection string from SignalR:Redis:ConnectionString.
    /// </summary>
    public static IServiceCollection AddRedisSignalRBackplane(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connStr = configuration["SignalR:Redis:ConnectionString"]
            ?? throw new InvalidOperationException(
                "SignalR:Redis:ConnectionString is not configured.");

        var channelPrefix = configuration["SignalR:Redis:ChannelPrefix"];

        services.AddSignalR(opts => opts.EnableDetailedErrors = true)
            .AddStackExchangeRedis(o =>
            {
                o.Configuration = StackExchange.Redis.ConfigurationOptions.Parse(connStr);
                if (channelPrefix is not null)
                    o.Configuration.ChannelPrefix =
                        StackExchange.Redis.RedisChannel.Literal(channelPrefix);
            });

        return services;
    }
}

public sealed class RedisBackplaneOptions
{
    public string ConnectionString { get; set; } = default!;
    public string? ChannelPrefix   { get; set; }
}
