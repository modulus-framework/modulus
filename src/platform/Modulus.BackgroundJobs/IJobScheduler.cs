namespace Modulus.BackgroundJobs;

public interface IJobScheduler
{
    Task EnqueueAsync<TJob, TArgs>(
        TArgs args,
        CancellationToken ct = default)
        where TJob : IBackgroundJob<TArgs>;

    Task ScheduleAsync<TJob, TArgs>(
        TArgs    args,
        TimeSpan delay,
        CancellationToken ct = default)
        where TJob : IBackgroundJob<TArgs>;

    void AddRecurring<TJob, TArgs>(
        string jobId,
        string cronExpression,
        TArgs  args)
        where TJob : IBackgroundJob<TArgs>;

    void RemoveRecurring(string jobId);
}