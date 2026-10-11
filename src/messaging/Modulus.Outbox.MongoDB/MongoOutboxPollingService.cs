namespace Modulus.Outbox.MongoDB;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Outbox.Abstractions;

/// <summary>
/// Hosted service that periodically invokes <see cref="MongoOutboxProcessor"/>. Mirrors the EF Core
/// <c>OutboxPollingService</c>: it drains a backlog without sleeping, honours leader election, and keeps polling after a
/// failure. Resolves the (scoped) processor from a fresh scope on every iteration to avoid the captive-dependency trap of a
/// singleton hosted service holding a scoped service.
/// </summary>
internal sealed class MongoOutboxPollingService(
    IServiceProvider sp,
    IOptions<OutboxOptions> opts,
    ILogger<MongoOutboxPollingService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Honoured at runtime: multiple AddOutbox/AddMongoOutbox calls merge
        // their options, so the flag's final value is only known after the
        // container is built.
        if (opts.Value.DisableAutoPolling)
            return;

        var interval = TimeSpan.FromSeconds(opts.Value.PollingIntervalSec);
        var leaderElection = opts.Value.EnableLeaderElection;

        while (!stoppingToken.IsCancellationRequested)
        {
            var drainAgain = false;
            try
            {
                // With leader election, only the replica holding the lock polls; the others idle until the lease expires.
                IAsyncDisposable? lease = null;
                if (leaderElection && sp.GetService<IDistributedLock>() is { } locks)
                {
                    lease = await locks.TryAcquireAsync(
                        "modulus:outbox:leader",
                        TimeSpan.FromSeconds(opts.Value.LockTimeoutSec),
                        stoppingToken);
                    if (lease is null)
                    {
                        await Task.Delay(interval, stoppingToken);
                        continue;
                    }
                }

                try
                {
                    await using var scope = sp.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<MongoOutboxProcessor>();
                    await processor.ProcessAsync(stoppingToken);
                    drainAgain = processor.HasMoreWork;
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
                // A transient failure must not end the loop: the outbox would silently stop draining until a restart.
                logger.LogError(ex, "MongoDB outbox processing iteration failed; will retry on next interval.");
            }

            // A full batch means a backlog: run again at once rather than capping throughput at BatchSize per interval.
            if (!drainAgain)
                await Task.Delay(interval, stoppingToken);
        }
    }
}
