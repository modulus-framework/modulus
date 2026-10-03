namespace Modulus.Webhooks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;

/// <summary>
/// Runs <see cref="WebhookDeliveryProcessor"/> cycles in the background (when <c>Webhooks:EnableDelivery</c> is on). A
/// cycle that sent a full batch is followed by the next one at once; otherwise it waits for the polling interval.
/// </summary>
internal sealed class WebhookDeliveryService(
    IServiceScopeFactory scopes,
    IOptions<ModulusWebhooksOptions> options,
    ILogger<WebhookDeliveryService> logger)
    : BackgroundService
{
    internal const string LeaderLockKey = "modulus:webhooks:leader";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.EnableDelivery)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            var attempted = 0;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                IAsyncDisposable? lease = null;
                if (settings.EnableLeaderElection && scope.ServiceProvider.GetService<IDistributedLock>() is { } leader)
                {
                    lease = await leader.TryAcquireAsync(LeaderLockKey, settings.LockDuration, stoppingToken);
                    if (lease is null)
                    {
                        await Task.Delay(settings.PollingInterval, stoppingToken);
                        continue;
                    }
                }

                try
                {
                    attempted = await scope.ServiceProvider.GetRequiredService<WebhookDeliveryProcessor>().ProcessAsync(stoppingToken);
                }
                finally
                {
                    if (lease is not null)
                        await lease.DisposeAsync();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A database or network outage must not stop deliveries for good; try again next interval.
                logger.LogError(ex, "Webhook delivery cycle failed; retrying after the polling interval.");
            }

            if (attempted < Math.Max(1, settings.BatchSize))
                await Task.Delay(settings.PollingInterval, stoppingToken);
        }
    }
}
