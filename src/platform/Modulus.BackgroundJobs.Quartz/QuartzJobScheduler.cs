using Quartz;
using Modulus.BackgroundJobs;
using Modulus.Core.Abstractions;

namespace Modulus.BackgroundJobs.Quartz;

/// <summary>
/// Durable background job scheduler backed by Quartz.NET.
/// Jobs are persisted to a relational database and executed reliably across
/// multiple replicas with built-in clustering, retry, and failure handling.
/// </summary>
public sealed class QuartzJobScheduler(
    ISchedulerFactory schedulerFactory,
    IServiceProvider serviceProvider) : IJobScheduler
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
        var jobDataMap = new JobDataMap
        {
            ["args"] = args!,
            ["tenantId"] = serviceProvider.GetService<ICurrentTenant>()?.TenantId?.ToString("N") ?? string.Empty,
            ["correlationId"] = serviceProvider.GetService<ICorrelationContext>()?.CorrelationId ?? string.Empty,
        };

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
        _ = Task.Run(() => AddRecurringAsync<TJob, TArgs>(jobId, cronExpression, args));
    }

    private async Task AddRecurringAsync<TJob, TArgs>(
        string jobId,
        string cronExpression,
        TArgs args)
        where TJob : IBackgroundJob<TArgs>
    {
        try
        {
            var scheduler = await GetSchedulerAsync();
            var jobDataMap = new JobDataMap { ["args"] = args! };

            var jobDetail = JobBuilder.Create<QuartzJobAdapter<TJob, TArgs>>()
                .WithIdentity(jobId, "recurring")
                .SetJobData(jobDataMap)
                .StoreDurably()
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity($"{jobId}_trigger", "recurring")
                .WithCronSchedule(cronExpression)
                .StartNow()
                .Build();

            var jobKey = jobDetail.Key;
            if (await scheduler.CheckExists(jobKey))
                await scheduler.RescheduleJob(trigger.Key, trigger);
            else
                await scheduler.ScheduleJob(jobDetail, trigger);
        }
        catch (Exception ex)
        {
            // Log but don't throw — AddRecurring is fire-and-forget, so schedule errors
            // won't propagate to the caller. This is consistent with the ChannelJobQueue.
            System.Diagnostics.Debug.WriteLine($"Failed to schedule recurring job: {ex}");
        }
    }

    public void RemoveRecurring(string jobId)
    {
        _ = Task.Run(() => RemoveRecurringAsync(jobId));
    }

    private async Task RemoveRecurringAsync(string jobId)
    {
        try
        {
            var scheduler = await GetSchedulerAsync();
            var jobKey = new JobKey(jobId, "recurring");
            if (await scheduler.CheckExists(jobKey))
                await scheduler.DeleteJob(jobKey);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to remove recurring job: {ex}");
        }
    }

    private static string GetJobName<TJob>() => typeof(TJob).Name;
}
