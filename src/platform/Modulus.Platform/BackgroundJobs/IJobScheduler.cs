namespace Modulus.BackgroundJobs;

/// <summary>
/// Registers and executes background jobs. The default <see cref="ChannelJobQueue"/>
/// implementation holds all work in memory — delayed jobs and recurring schedules
/// are lost on process shutdown and recurring jobs fire on every replica
/// independently. Use this for <b>dev/test only</b>.
/// <para>
/// For production multi-replica deployments, replace the registration with a
/// durable job scheduler (Quartz.NET, Hangfire) that persists to a store and
/// coordinates across instances.
/// </para>
/// </summary>
public interface IJobScheduler
{
    /// <summary>
    /// Enqueues a job to run as soon as a worker is available.
    /// Backed by the in-memory <see cref="ChannelJobQueue"/> by default —
    /// enqueued jobs are lost on restart.
    /// </summary>
    Task EnqueueAsync<TJob, TArgs>(
        TArgs args,
        CancellationToken ct = default)
        where TJob : IBackgroundJob<TArgs>;

    /// <summary>
    /// Schedules a job to run after <paramref name="delay"/> elapses.
    /// <para>
    /// <b>⚠️ Durability boundary:</b> The delay is held in memory as a
    /// <c>Task.Delay</c>. The job is lost if the process stops before the delay
    /// completes — there is no persistence and no recovery on restart.
    /// </para>
    /// Not suitable for production work that must survive shutdown.
    /// For durable delayed jobs, adopt Quartz.NET or Hangfire.
    /// </summary>
    Task ScheduleAsync<TJob, TArgs>(
        TArgs args,
        TimeSpan delay,
        CancellationToken ct = default)
        where TJob : IBackgroundJob<TArgs>;

    /// <summary>
    /// Registers a recurring job to run on a cron schedule.
    /// <para>
    /// <b>⚠️ Durability and clustering boundary:</b> The schedule is held in
    /// memory on each process. In a multi-replica deployment:
    /// <list type="bullet">
    /// <item>Each replica independently tracks the cron expression and fires the
    /// job when its local clock thinks it's due — the job runs once per replica
    /// per cron tick, not once per tick cluster-wide.</item>
    /// <item>Schedules are lost on process shutdown with no recovery.</item>
    /// </list>
    /// </para>
    /// Not suitable for production multi-instance deployments. For correct
    /// per-cluster-tick execution, adopt Quartz.NET (clustering via DB) or
    /// Hangfire.
    /// </summary>
    void AddRecurring<TJob, TArgs>(
        string jobId,
        string cronExpression,
        TArgs args)
        where TJob : IBackgroundJob<TArgs>;

    void RemoveRecurring(string jobId);
}
