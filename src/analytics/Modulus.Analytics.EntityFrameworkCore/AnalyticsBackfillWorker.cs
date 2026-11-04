namespace Modulus.Analytics.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Runs <see cref="AnalyticsBackfillProcessor"/> cycles in the background (when <c>Analytics:Backfill:EnableWorker</c> is on). A cycle that
/// worked on runs is followed by the next at once; otherwise the worker waits for the polling interval.
/// </summary>
internal sealed class AnalyticsBackfillWorker(
    IServiceScopeFactory scopes,
    IOptions<AnalyticsBackfillOptions> options,
    ILogger<AnalyticsBackfillWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.EnableWorker)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var worked = 0;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                worked = await scope.ServiceProvider.GetRequiredService<AnalyticsBackfillProcessor>().ProcessAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Analytics backfill cycle failed; retrying after the polling interval.");
            }

            if (worked == 0)
            {
                await Task.Delay(settings.PollingInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
