namespace Modulus.Outbox.MongoDB;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Outbox.Abstractions;

/// <summary>
/// Hosted service that periodically invokes <see cref="MongoOutboxProcessor"/>.
/// Resolves the (scoped) processor from a fresh scope on every iteration to
/// avoid the captive-dependency trap of a singleton hosted service holding a
/// scoped service.
/// </summary>
internal sealed class MongoOutboxPollingService(
    IServiceProvider sp,
    IOptions<OutboxOptions> opts,
    ILogger<MongoOutboxPollingService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(opts.Value.PollingIntervalSec);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = sp.CreateAsyncScope();
                var processor = scope.ServiceProvider
                    .GetRequiredService<MongoOutboxProcessor>();
                await processor.ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "MongoDB outbox processing iteration failed; will retry on next interval.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
