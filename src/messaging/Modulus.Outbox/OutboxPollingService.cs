using Modulus.Outbox.Abstractions;

namespace Modulus.Outbox;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class OutboxPollingService(
    IServiceProvider sp,
    IOptions<OutboxOptions> opts,
    ILogger<OutboxPollingService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(opts.Value.PollingIntervalSec);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Resolve the scoped OutboxProcessor from a fresh scope on
                // every iteration. BackgroundService is effectively singleton;
                // constructor-injecting a scoped processor would capture a
                // single scope for the process lifetime (captive dependency).
                await using var scope = sp.CreateAsyncScope();
                var processor = scope.ServiceProvider
                    .GetRequiredService<OutboxProcessor>();
                await processor.ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // A transient DB failure, broker outage, or any other error
                // must NOT terminate the polling loop — otherwise the outbox
                // silently stops draining until process restart. Log and
                // retry on the next interval.
                logger.LogError(ex,
                    "Outbox processing iteration failed; will retry on next interval.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
