namespace Modulus.Tenancy.Lifecycle;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Runs the lifecycle in the background: active operations (pending, running, on hold) every <see cref="TenantLifecycleOptions.WorkerInterval"/>,
/// and the retention sweep every <see cref="TenantLifecycleOptions.RetentionSweepInterval"/>. A failed operation stays failed until it is
/// run again, so a broken configuration is not retried in a loop.
/// </summary>
internal sealed class TenantLifecycleWorker(
    IServiceScopeFactory scopes,
    IOptions<TenantLifecycleOptions> options,
    TimeProvider clock,
    ILogger<TenantLifecycleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastSweep = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var provider = scope.ServiceProvider.GetRequiredService<ITenantLifecycleProvider>();
                var store = scope.ServiceProvider.GetRequiredService<ITenantLifecycleStore>();

                foreach (var operation in await store.ListActiveOperationsAsync(stoppingToken))
                {
                    try
                    {
                        await provider.RunOperationAsync(operation.Id, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception)
                    {
                        // The provider has recorded the failure on the operation; the exception type is logged there.
                        logger.LogError("Tenant lifecycle operation {OperationId} failed and stays failed until it is run again", operation.Id);
                    }
                }

                var now = clock.GetUtcNow();
                if (options.Value.RetentionDriver == RetentionDriver.Worker && now - lastSweep >= options.Value.RetentionSweepInterval)
                {
                    await provider.RunRetentionAsync(stoppingToken);
                    lastSweep = now;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError("Tenant lifecycle tick failed ({Error})", ex.GetType().Name);
            }

            await Task.Delay(options.Value.WorkerInterval, stoppingToken);
        }
    }
}
