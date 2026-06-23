using global::Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace Modulus.BackgroundJobs.Hangfire;

/// <summary>
/// IJobScheduler implementation backed by Hangfire.
/// </summary>
internal sealed class HangfireJobScheduler(
    IBackgroundJobClient backgroundJobClient,
    IRecurringJobManager recurringJobManager)
    : IJobScheduler
{
    public Task EnqueueAsync<TJob, TArgs>(
        TArgs args, CancellationToken ct = default)
        where TJob : IBackgroundJob<TArgs>
    {
        backgroundJobClient.Enqueue<HangfireJobWrapper<TJob, TArgs>>(
            w => w.ExecuteAsync(args, CancellationToken.None));
        return Task.CompletedTask;
    }

    public Task ScheduleAsync<TJob, TArgs>(
        TArgs args, TimeSpan delay, CancellationToken ct = default)
        where TJob : IBackgroundJob<TArgs>
    {
        backgroundJobClient.Schedule<HangfireJobWrapper<TJob, TArgs>>(
            w => w.ExecuteAsync(args, CancellationToken.None),
            delay);
        return Task.CompletedTask;
    }

    public void AddRecurring<TJob, TArgs>(
        string jobId, string cronExpression, TArgs args)
        where TJob : IBackgroundJob<TArgs>
    {
        var job = global::Hangfire.Common.Job.FromExpression<HangfireJobWrapper<TJob, TArgs>>(
            w => w.ExecuteAsync(args, CancellationToken.None));

        recurringJobManager.AddOrUpdate(jobId, job, cronExpression,
            new RecurringJobOptions());
    }

    public void RemoveRecurring(string jobId)
    {
        recurringJobManager.RemoveIfExists(jobId);
    }
}

/// <summary>
/// DI-resolvable wrapper that executes the actual IBackgroundJob.
/// </summary>
public class HangfireJobWrapper<TJob, TArgs>(
    IServiceProvider sp)
    where TJob : IBackgroundJob<TArgs>
{
    public async Task ExecuteAsync(TArgs args, CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<TJob>();
        await job.ExecuteAsync(args, ct);
    }
}
