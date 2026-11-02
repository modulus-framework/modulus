namespace Modulus.Tenancy.Lifecycle;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modulus.BackgroundJobs;

/// <summary>The arguments of the retention job (none needed: the sweep decides what is due).</summary>
public sealed record TenantRetentionArgs;

/// <summary>
/// The retention sweep as a background job: <see cref="ITenantLifecycleProvider.RunRetentionAsync"/> on the platform's job scheduler.
/// With the Quartz scheduler it is a durable, clustered schedule; with the in-memory scheduler it runs in-process. The sweep takes the
/// cluster lock, so several nodes firing the same tick run it once.
/// </summary>
public sealed class TenantRetentionJob(ITenantLifecycleProvider provider) : IBackgroundJob<TenantRetentionArgs>
{
    /// <summary>The job id used when the schedule is registered.</summary>
    public const string JobId = "tenant-lifecycle-retention";

    /// <inheritdoc />
    public Task ExecuteAsync(TenantRetentionArgs args, CancellationToken ct) => provider.RunRetentionAsync(ct);
}

/// <summary>Registers the retention job on the platform scheduler when <see cref="RetentionDriver.Scheduler"/> is configured.</summary>
internal sealed class TenantRetentionScheduleService(
    IServiceProvider services,
    IOptions<TenantLifecycleOptions> options) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (settings.RetentionDriver != RetentionDriver.Scheduler)
            return Task.CompletedTask;

        var scheduler = services.GetService<IJobScheduler>()
            ?? throw new InvalidOperationException(
                "TenantLifecycle:RetentionDriver is Scheduler, but no job scheduler is registered (AddBackgroundJobs or AddQuartzJobScheduler).");
        scheduler.AddRecurring<TenantRetentionJob, TenantRetentionArgs>(TenantRetentionJob.JobId, settings.RetentionCron, new TenantRetentionArgs());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
