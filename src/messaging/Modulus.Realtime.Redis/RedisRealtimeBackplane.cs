namespace Modulus.Realtime.Redis;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

/// <summary>Settings of <c>Realtime:Redis</c>.</summary>
public sealed class RealtimeRedisOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Realtime:Redis";

    /// <summary>The Redis connection string; falls back to <c>Caching:Redis:ConnectionString</c>. Ignored when an <see cref="IConnectionMultiplexer"/> is already registered.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The pub/sub channel. Every node publishing and delivering the same messages uses the same channel: one per app (or
    /// per service whose clients connect to it); give unrelated apps sharing a Redis different channels.
    /// </summary>
    public string Channel { get; set; } = "modulus:realtime";
}

/// <summary>Registration of the Redis realtime backplane.</summary>
public static class RedisRealtimeExtensions
{
    /// <summary>
    /// Replaces the single-node backplane of <c>AddModulusRealtime</c> with Redis pub/sub (settings <c>Realtime:Redis</c>), so a
    /// message published on any replica reaches clients connected to every replica. Reuses a registered
    /// <see cref="IConnectionMultiplexer"/> (e.g. the cache's), else connects with <c>AbortOnConnectFail=false</c>: the app
    /// starts while Redis is down and messages are then delivered on the publishing node only.
    /// </summary>
    public static IServiceCollection AddRedisRealtimeBackplane(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<RealtimeRedisOptions>()
            .Bind(configuration.GetSection(RealtimeRedisOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Channel), "Realtime:Redis:Channel is required.");

        var connectionString = configuration[$"{RealtimeRedisOptions.SectionName}:ConnectionString"]
            ?? configuration["Caching:Redis:ConnectionString"];
        services.TryAddSingleton<IConnectionMultiplexer>(_ =>
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Realtime:Redis:ConnectionString (or Caching:Redis:ConnectionString) is required for the Redis realtime backplane.");
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });

        services.TryAddSingleton<RedisRealtimeBackplane>();
        services.Replace(ServiceDescriptor.Singleton<IRealtimeBackplane>(sp => sp.GetRequiredService<RedisRealtimeBackplane>()));
        services.AddHostedService(sp => sp.GetRequiredService<RedisRealtimeBackplane>());
        return services;
    }
}

/// <summary>
/// Publishes realtime messages to a Redis channel every node subscribes to; each node delivers what it receives to its own
/// connections, in order (one sequential subscription queue). When Redis is unreachable a publish is delivered on this node
/// only, so single-node clients keep working.
/// </summary>
internal sealed partial class RedisRealtimeBackplane(
    IConnectionMultiplexer redis,
    IRealtimeDispatcher dispatcher,
    IOptions<RealtimeRedisOptions> options,
    ILogger<RedisRealtimeBackplane> logger)
    : IRealtimeBackplane, IHostedService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private readonly CancellationTokenSource _stopping = new();
    private ChannelMessageQueue? _queue;
    private Task? _subscribing;

    private RedisChannel Channel => RedisChannel.Literal(options.Value.Channel);

    public async Task PublishAsync(RealtimeMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            await redis.GetSubscriber().PublishAsync(Channel, message.ToJson()).WaitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogPublishFailed(logger, ex, message.Id);
            await dispatcher.DispatchAsync(message, ct).ConfigureAwait(false);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscribing = SubscribeAsync(_stopping.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        if (_subscribing is not null)
            await _subscribing.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (_queue is not null)
            await _queue.UnsubscribeAsync().ConfigureAwait(false);
        _stopping.Dispose();
    }

    // Retries until the first subscription succeeds; StackExchange.Redis restores it after later reconnects.
    private async Task SubscribeAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                var queue = await redis.GetSubscriber().SubscribeAsync(Channel).ConfigureAwait(false);
                queue.OnMessage(DeliverAsync);
                _queue = queue;
                return;
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                LogSubscribeFailed(logger, ex, options.Value.Channel);
            }

            try
            {
                await Task.Delay(RetryDelay, stopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task DeliverAsync(ChannelMessage received)
    {
        if (RealtimeMessage.FromJson(received.Message.ToString()) is not { } message)
        {
            LogMalformed(logger, options.Value.Channel);
            return;
        }

        try
        {
            await dispatcher.DispatchAsync(message, _stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Realtime message {MessageId} could not be published to Redis; delivered on this node only.")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, string messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Subscribing to realtime channel {Channel} failed; retrying.")]
    private static partial void LogSubscribeFailed(ILogger logger, Exception exception, string channel);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored a message on realtime channel {Channel} that is not a realtime message.")]
    private static partial void LogMalformed(ILogger logger, string channel);
}
