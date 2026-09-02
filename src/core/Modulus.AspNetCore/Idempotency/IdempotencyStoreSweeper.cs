namespace Modulus.AspNetCore.Idempotency;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

/// <summary>
/// Background service that periodically evicts expired entries from the
/// <see cref="InMemoryIdempotencyStore"/>, bounding memory consumption.
/// Runs every 60 seconds by default. The sweep interval is slightly shorter
/// than the retention window to prevent unbounded growth.
/// </summary>
internal sealed class IdempotencyStoreSweeper(
    IServiceProvider serviceProvider,
    IOptions<IdempotencyOptions> options,
    TimeProvider? clock = null) : BackgroundService
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sweepInterval = TimeSpan.FromSeconds(Math.Max(30, options.Value.RetentionSeconds / 2));

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(sweepInterval, stoppingToken);

            try
            {
                // Resolve from a fresh scope to avoid captive dependencies
                using var scope = serviceProvider.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
                var cutoff = _clock.GetUtcNow();
                await store.PurgeExpiredAsync(cutoff, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Swallow — sweeper is best-effort; transient DI errors should
                // not crash the host. Next cycle will retry.
            }
        }
    }
}
