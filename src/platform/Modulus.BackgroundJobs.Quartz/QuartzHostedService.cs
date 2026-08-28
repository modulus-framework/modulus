using Microsoft.Extensions.Hosting;
using Quartz;

namespace Modulus.BackgroundJobs.Quartz;

/// <summary>
/// Hosted service that manages the Quartz.NET scheduler lifecycle.
/// Starts the scheduler on app startup and gracefully shuts it down on app stop.
/// </summary>
internal sealed class QuartzHostedService(ISchedulerFactory schedulerFactory) : IHostedService
{
    private IScheduler? _scheduler;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        await _scheduler.Start(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_scheduler is not null)
            await _scheduler.Shutdown(waitForJobsToComplete: true, cancellationToken);
    }
}
