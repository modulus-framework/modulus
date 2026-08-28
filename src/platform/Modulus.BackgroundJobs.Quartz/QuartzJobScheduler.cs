using Quartz;
using Modulus.BackgroundJobs;

namespace Modulus.BackgroundJobs.Quartz;

/// <summary>
/// Durable background job scheduler backed by Quartz.NET.
/// Jobs are persisted to a relational database and executed reliably across
/// multiple replicas with built-in clustering, retry, and failure handling.
/// </summary>
public sealed class QuartzJobScheduler(ISchedulerFactory schedulerFactory) : IJobScheduler
{
    private IScheduler? _scheduler;

    private async Task<IScheduler> GetSchedulerAsync()
    {
        if (_scheduler is null)
            _scheduler = await schedulerFactory.GetScheduler();
        return _scheduler;
    }

    public async Task EnqueueAsync<TJob, TArgs>(
        TArgs args,
        CancellationToken ct = default)
        where TJob : IBackgroundJob<TArgs>
    {
        var scheduler = await GetSchedulerAsync();
        var jobName = GetJobName<TJob>();
        var jobDataMap = new JobDataMap { ["args"] = args! };

        var jobDetail = JobBuilder.Create<QuartzJobAdapter<TJob, TArgs>>()
            .WithIdentity(jobName, Guid.NewGuid().ToString())
            .SetJobData(jobDataMap)
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{jobName}_trigger_{Guid.NewGuid()}")
            .StartNow()
            .Build();

        await scheduler.ScheduleJob(jobDetail, trigger, ct);
    }

    public async Task ScheduleAsync<TJob, TArgs>(
        TArgs args,
        TimeSpan delay,
        CancellationToken ct = default)
        where TJob : IBackgroundJob<TArgs>
    {
        var scheduler = await GetSchedulerAsync();
        var jobName = GetJobName<TJob>();
        var jobDataMap = new JobDataMap { ["args"] = args! };

        var jobDetail = JobBuilder.Create<QuartzJobAdapter<TJob, TArgs>>()
            .WithIdentity(jobName, Guid.NewGuid().ToString())
            .SetJobData(jobDataMap)
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{jobName}_trigger_{Guid.NewGuid()}")
            .StartAt(DateTimeOffset.UtcNow.Add(delay))
            .Build();

        await scheduler.ScheduleJob(jobDetail, trigger, ct);
    }

    public void AddRecurring<TJob, TArgs>(
        string jobId,
        string cronExpression,
        TArgs args)
        where TJob : IBackgroundJob<TArgs>
    {
        throw new NotImplementedException(
            "Recurring jobs must be configured directly via Quartz.NET APIs. " +
            "Call AddQuartzJobScheduler() with a configurator that sets up " +
            "recurring job triggers before the app starts.");
    }

    public void RemoveRecurring(string jobId)
    {
        throw new NotImplementedException(
            "RemoveRecurring is not yet implemented in the Quartz adapter. " +
            "Manage recurring jobs directly via Quartz.NET.");
    }

    private static string GetJobName<TJob>() => typeof(TJob).Name;
}
