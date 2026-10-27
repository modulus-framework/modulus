using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Modulus.BackgroundJobs.Quartz;

/// <summary>
/// Hosted service that manages the Quartz.NET scheduler lifecycle.
/// Starts the scheduler on app startup and gracefully shuts it down on app stop.
/// </summary>
internal sealed class QuartzHostedService(
    ISchedulerFactory schedulerFactory,
    IHostEnvironment environment,
    IOptions<QuartzOptions> quartzOptions,
    ILogger<QuartzHostedService> logger) : IHostedService
{
    private IScheduler? _scheduler;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (environment.IsProduction() && IsInMemory(quartzOptions.Value))
            logger.LogWarning(
                "Background jobs use the in-memory Quartz store in Production: queued jobs are lost on restart and are not shared between replicas. Configure UseModulusSqliteStore or UseModulusPostgresStore.");

        _scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        await _scheduler.Start(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_scheduler is not null)
            await _scheduler.Shutdown(waitForJobsToComplete: true, cancellationToken);
    }

    // Quartz records the job store class in its configuration. The in-memory store is RAMJobStore; the ADO.NET store
    // (UseModulusSqliteStore, UseModulusPostgresStore) is a JobStoreTX.
    private static bool IsInMemory(QuartzOptions options) =>
        options["quartz.jobStore.type"] is not { } type || type.Contains("RAMJobStore", StringComparison.Ordinal);
}
