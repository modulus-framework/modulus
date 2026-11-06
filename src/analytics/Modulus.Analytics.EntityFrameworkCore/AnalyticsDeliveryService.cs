namespace Modulus.Analytics.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Runs <see cref="AnalyticsDeliveryProcessor"/> cycles in the background (when <c>Analytics:Delivery:EnableDelivery</c> is on). A cycle
/// that picked up a full batch is followed by the next one at once; otherwise it waits for the polling interval.
/// </summary>
internal sealed class AnalyticsDeliveryService(
    IServiceScopeFactory scopes,
    IOptions<AnalyticsDeliveryOptions> options,
    ILogger<AnalyticsDeliveryService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.EnableDelivery)
        {
            return;
        }

        var nextRefresh = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            var picked = 0;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                picked = await scope.ServiceProvider.GetRequiredService<AnalyticsDeliveryProcessor>().ProcessAsync(stoppingToken).ConfigureAwait(false);
                if (DateTime.UtcNow >= nextRefresh)
                {
                    AnalyticsMetrics.Publish(await scope.ServiceProvider.GetRequiredService<IAnalyticsFreshnessReader>().ReadAsync(stoppingToken).ConfigureAwait(false));
                    nextRefresh = DateTime.UtcNow + settings.FreshnessRefreshInterval;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A database outage must not stop analytics delivery for good; try again next interval.
                logger.LogError(ex, "Analytics delivery cycle failed; retrying after the polling interval.");
            }

            if (picked < Math.Max(1, settings.BatchSize))
            {
                await Task.Delay(settings.PollingInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
