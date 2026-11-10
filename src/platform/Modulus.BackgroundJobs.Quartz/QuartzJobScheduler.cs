using Quartz;
using Microsoft.Extensions.Logging;
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
    IServiceProvider serviceProvider,
    ILogger<QuartzJobScheduler> logger) : IJobScheduler
{
    private IScheduler? _scheduler;

    private async Task<IScheduler> GetSchedulerAsync()
    {
        if (_scheduler is null)
            _scheduler = await schedulerFactory.GetScheduler();
        return _scheduler;
    }

    /// <summary>
    /// Builds a JobDataMap carrying <paramref name="args"/> plus the CURRENT
    /// ambient tenant/correlation context, restored by
    /// <see cref="QuartzJobAdapter{TJob,TArgs}"/> when the job fires. Shared by
    /// every scheduling path (immediate, delayed, recurring) so none of them
    /// can silently drift out of sync with the others — a job that fires
    /// without this context runs with NO ambient tenant, which is a
    /// cross-tenant data-isolation risk for any handler that touches a
    /// tenant-scoped <c>ModuleDbContext</c>.
    /// </summary>
    private JobDataMap BuildJobDataMap<TArgs>(TArgs args, string systemName, bool queuedByPerson = true)
    {
        var map = new JobDataMap
        {
            // A persistent store reads a stored value back as a string, a number, a boolean or a string dictionary, so an arguments
            // record cannot be kept as an object: it is stored as JSON text and read back by the adapter.
            ["argsJson"] = System.Text.Json.JsonSerializer.Serialize(args),
            ["tenantId"] = serviceProvider.GetService<ICurrentTenant>()?.TenantId?.ToString("N") ?? string.Empty,
            ["correlationId"] = serviceProvider.GetService<ICorrelationContext>()?.CorrelationId ?? string.Empty,
        };

        // The signed-in person who queued the job is its actor, and the adapter checks they may still start access (A6-1). Otherwise the
        // job runs as the system, named by systemName (A6-2): the job type for an ad hoc job, the job id for a recurring one.
        if (queuedByPerson && serviceProvider.GetService<ICurrentUser>() is { IsAuthenticated: true, UserId: { } userId })
        {
            map["initiatorId"] = userId.ToString("N");
            map["actor"] = JobActor.ForPerson(userId);
        }
        else
        {
            map["actor"] = JobActor.ForSystem(systemName);
        }

        return map;
    }

    public async Task EnqueueAsync<TJob, TArgs>(
        TArgs args,
        CancellationToken ct = default)
        where TJob : IBackgroundJob<TArgs>
    {
        var scheduler = await GetSchedulerAsync();
        var jobName = GetJobName<TJob>();

        var jobDetail = JobBuilder.Create<QuartzJobAdapter<TJob, TArgs>>()
            .WithIdentity(jobName, Guid.NewGuid().ToString())
            .SetJobData(BuildJobDataMap(args, jobName))
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

        var jobDetail = JobBuilder.Create<QuartzJobAdapter<TJob, TArgs>>()
            .WithIdentity(jobName, Guid.NewGuid().ToString())
            .SetJobData(BuildJobDataMap(args, jobName))
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{jobName}_trigger_{Guid.NewGuid()}")
            .StartAt(DateTimeOffset.UtcNow.Add(delay))
            .Build();

        await scheduler.ScheduleJob(jobDetail, trigger, ct);
    }

    /// <summary>
    /// Schedules a cron job. <paramref name="firstRunAt"/> is the earliest time the schedule may fire (Quartz's <c>StartAt</c>); the
    /// first run is then the first cron time at or after it. Null starts the schedule now.
    /// </summary>
    public void AddRecurring<TJob, TArgs>(
        string jobId,
        string cronExpression,
        TArgs args,
        DateTime? firstRunAt = null)
        where TJob : IBackgroundJob<TArgs>
    {
        _ = AddRecurringAsync<TJob, TArgs>(jobId, cronExpression, args, firstRunAt);
    }

    private const int RecurringScheduleAttempts = 4;

    private async Task AddRecurringAsync<TJob, TArgs>(
        string jobId,
        string cronExpression,
        TArgs args,
        DateTime? firstRunAt)
        where TJob : IBackgroundJob<TArgs>
    {
        // AddRecurring is fire-and-forget, so a failure cannot reach the caller. A transient store error (database
        // restart, lock timeout) is retried with back-off; a bad cron expression cannot succeed, so it fails at once.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ScheduleRecurringOnceAsync<TJob, TArgs>(jobId, cronExpression, args, firstRunAt);
                return;
            }
            catch (FormatException ex)
            {
                logger.LogCritical(ex, "Recurring job {JobId} was NOT scheduled: invalid cron expression '{Cron}'", jobId, cronExpression);
                return;
            }
            catch (Exception ex) when (attempt < RecurringScheduleAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                logger.LogWarning(ex, "Scheduling recurring job {JobId} failed (attempt {Attempt}/{Max}); retrying in {Delay}",
                    jobId, attempt, RecurringScheduleAttempts, delay);
                await Task.Delay(delay);
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Recurring job {JobId} was NOT scheduled after {Max} attempts and will not run until the process restarts",
                    jobId, RecurringScheduleAttempts);
                return;
            }
        }
    }

    private async Task ScheduleRecurringOnceAsync<TJob, TArgs>(
        string jobId,
        string cronExpression,
        TArgs args,
        DateTime? firstRunAt)
        where TJob : IBackgroundJob<TArgs>
    {
        var scheduler = await GetSchedulerAsync();

        var jobDetail = JobBuilder.Create<QuartzJobAdapter<TJob, TArgs>>()
            .WithIdentity(jobId, "recurring")
            .SetJobData(BuildJobDataMap(args, jobId, queuedByPerson: false))
            .StoreDurably()
            .Build();

        var start = firstRunAt is { } earliest
            ? new DateTimeOffset(DateTime.SpecifyKind(earliest, DateTimeKind.Utc))
            : DateTimeOffset.UtcNow;
        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{jobId}_trigger", "recurring")
            .WithCronSchedule(cronExpression)
            .StartAt(start)
            .Build();

        if (await scheduler.CheckExists(jobDetail.Key))
            await scheduler.RescheduleJob(trigger.Key, trigger);
        else
            await scheduler.ScheduleJob(jobDetail, trigger);
    }

    public void RemoveRecurring(string jobId)
    {
        _ = RemoveRecurringAsync(jobId);
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
            logger.LogError(ex, "Failed to remove recurring job {JobId}", jobId);
        }
    }

    private static string GetJobName<TJob>() => typeof(TJob).Name;
}
