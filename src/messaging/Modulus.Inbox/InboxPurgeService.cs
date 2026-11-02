namespace Modulus.Inbox;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Inbox.Abstractions;

/// <summary>
/// Runs <see cref="InboxPurge"/> every <see cref="InboxOptions.PurgeIntervalMinutes"/>. A failed pass is logged and retried at the
/// next interval; it never stops the host.
/// </summary>
internal sealed class InboxPurgeService(
    IServiceProvider sp,
    IOptions<InboxOptions> opts,
    ILogger<InboxPurgeService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var deleted = await InboxPurge.RunAsync(sp, opts.Value, logger, stoppingToken);
                if (deleted > 0)
                    logger.LogInformation("Inbox purge removed {Count} processed rows.", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Inbox purge pass failed; it is retried at the next interval.");
            }

            await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, opts.Value.PurgeIntervalMinutes)), stoppingToken);
        }
    }
}
