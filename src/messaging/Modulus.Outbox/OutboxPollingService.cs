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
        // Honoured at runtime: multiple AddOutbox calls merge their options, so
        // the flag's final value is only known after the container is built.
        if (opts.Value.DisableAutoPolling)
            return;

        var interval = TimeSpan.FromSeconds(opts.Value.PollingIntervalSec);
        var leaderElection = opts.Value.EnableLeaderElection;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // When leader election is enabled, only the replica that holds
                // the distributed lock polls the outbox — other replicas idle
                // until the lease expires. The row-level claim already provides
                // at-least-once correctness; this merely reduces redundant
                // cross-replica DB reads.
                IAsyncDisposable? lease = null;
                if (leaderElection)
                {
                    var lockService = sp.GetService<Core.Abstractions.IDistributedLock>();
                    if (lockService is not null)
                    {
                        lease = await lockService.TryAcquireAsync(
                            "modulus:outbox:leader",
                            TimeSpan.FromSeconds(opts.Value.LockTimeoutSec),
                            stoppingToken);
                        if (lease is null)
                        {
                            await Task.Delay(interval, stoppingToken);
                            continue;
                        }
                    }
                }

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
                finally
                {
                    if (lease is not null)
                        await lease.DisposeAsync();
                }
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
