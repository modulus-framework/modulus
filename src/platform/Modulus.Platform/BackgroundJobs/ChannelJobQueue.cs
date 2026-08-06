namespace Modulus.BackgroundJobs;

using System.Threading.Channels;
using Cronos;
using Microsoft.Extensions.Hosting;

internal sealed record JobEnvelope(
    Type JobType,
    Type ArgsType,
    object Args,
    TimeSpan Delay = default);

internal sealed record RecurringEntry(
    JobEnvelope Envelope,
    CronExpression Cron,
    DateTime NextRun);

public sealed class ChannelJobQueue(
    IServiceProvider sp,
    ILogger<ChannelJobQueue> logger)
    : IJobScheduler, IHostedService
{
    private readonly Channel<JobEnvelope> _channel =
        Channel.CreateUnbounded<JobEnvelope>(
            new UnboundedChannelOptions
            { SingleReader = false, SingleWriter = false });

    private readonly Dictionary<string, RecurringEntry> _recurring = [];
    private readonly Lock _recurringLock = new();
    private CancellationTokenSource? _cts;

    // Tracked so StopAsync can await in-flight workers for graceful shutdown.
    private Task? _recurringTask;
    private readonly List<Task> _workers = [];

    // ── IJobScheduler ─────────────────────────────────────────────
    public async Task EnqueueAsync<TJob, TArgs>(
        TArgs args, CancellationToken ct)
        where TJob : IBackgroundJob<TArgs>
    {
        var envelope = new JobEnvelope(
            typeof(TJob), typeof(TArgs), args!);
        await _channel.Writer.WriteAsync(envelope, ct);
    }

    public Task ScheduleAsync<TJob, TArgs>(
        TArgs args, TimeSpan delay, CancellationToken ct)
        where TJob : IBackgroundJob<TArgs>
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(delay, ct);
            await _channel.Writer.WriteAsync(
                new JobEnvelope(typeof(TJob), typeof(TArgs), args!), ct);
        }, ct);
        return Task.CompletedTask;
    }

    public void AddRecurring<TJob, TArgs>(
        string jobId, string cronExpression, TArgs args)
        where TJob : IBackgroundJob<TArgs>
    {
        var envelope = new JobEnvelope(typeof(TJob), typeof(TArgs), args!);
        var cron = CronExpression.Parse(cronExpression);
        var nextRun = cron.GetNextOccurrence(
            DateTimeOffset.UtcNow, TimeZoneInfo.Utc);

        lock (_recurringLock)
        {
            _recurring[jobId] = new RecurringEntry(
                envelope, cron, nextRun?.UtcDateTime ?? DateTime.UtcNow);
        }

        logger.LogInformation(
            "Recurring job {JobId} scheduled with '{Cron}', next run at {NextRun:O}",
            jobId, cronExpression, nextRun);
    }

    public void RemoveRecurring(string jobId)
    {
        lock (_recurringLock)
        {
            _recurring.Remove(jobId);
        }
    }

    // ── IHostedService ────────────────────────────────────────────
    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Recurring-job scheduler — checks every 30 seconds
        _recurringTask = Task.Run(() => RecurringSchedulerAsync(_cts.Token), _cts.Token);

        // Worker pool
        var workerCount = Math.Max(1, Environment.ProcessorCount / 2);
        for (var i = 0; i < workerCount; i++)
            _workers.Add(Task.Run(() => ProcessLoopAsync(_cts.Token), _cts.Token));

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_cts is not null)
            await _cts.CancelAsync();
        _channel.Writer.Complete();

        var pending = _workers.ToList();
        if (_recurringTask is not null)
            pending.Add(_recurringTask);

        // Give in-flight workers a bounded grace period to drain instead of
        // returning immediately and orphaning running jobs.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await Task.WhenAll(pending).WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "Background job workers did not stop within the shutdown timeout.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "One or more background job workers faulted during shutdown.");
        }
    }

    // ── Recurring scheduler ───────────────────────────────────────
    private async Task RecurringSchedulerAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var now = DateTime.UtcNow;
            var toEnqueue = new List<(string JobId, JobEnvelope Envelope, DateTime NextRun)>();

            lock (_recurringLock)
            {
                foreach (var (jobId, entry) in _recurring)
                {
                    if (entry.NextRun > now)
                        continue;

                    var next = entry.Cron.GetNextOccurrence(
                        DateTimeOffset.UtcNow, TimeZoneInfo.Utc);

                    toEnqueue.Add((jobId, entry.Envelope,
                        next?.UtcDateTime ?? now.AddHours(1)));
                }

                // Update next-run times
                foreach (var (jobId, _, nextRun) in toEnqueue)
                    _recurring[jobId] = _recurring[jobId] with { NextRun = nextRun };
            }

            foreach (var (jobId, envelope, _) in toEnqueue)
            {
                try
                {
                    await _channel.Writer.WriteAsync(envelope, ct);
                    logger.LogInformation("Recurring job {JobId} enqueued", jobId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to enqueue recurring job {JobId}", jobId);
                }
            }
        }
    }

    // ── Worker ────────────────────────────────────────────────────
    private async Task ProcessLoopAsync(CancellationToken ct)
    {
        await foreach (var envelope in _channel.Reader.ReadAllAsync(ct))
        {
            await using var scope = sp.CreateAsyncScope();
            try
            {
                // Resolve via the closed generic IBackgroundJob<TArgs> interface
                // so we can dispatch through the interface without dynamic/DLR.
                var jobInterface = typeof(IBackgroundJob<>).MakeGenericType(envelope.ArgsType);
                var job = scope.ServiceProvider.GetRequiredService(jobInterface);
                var invoker = s_jobInvokers.GetOrAdd(envelope.ArgsType, CompileJobInvoker);
                await invoker(job, envelope.Args, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job {Type} failed",
                    envelope.JobType.Name);
            }
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type,
        Func<object, object, CancellationToken, Task>> s_jobInvokers = new();

    private static Func<object, object, CancellationToken, Task> CompileJobInvoker(Type argsType)
    {
        var jobType = typeof(IBackgroundJob<>).MakeGenericType(argsType);
        var method = jobType.GetMethod("ExecuteAsync")!;
        var jobParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "job");
        var argParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "args");
        var ctParam = System.Linq.Expressions.Expression.Parameter(typeof(CancellationToken), "ct");
        var castJob = System.Linq.Expressions.Expression.Convert(jobParam, jobType);
        var castArg = System.Linq.Expressions.Expression.Convert(argParam, argsType);
        var call = System.Linq.Expressions.Expression.Call(castJob, method, castArg, ctParam);
        return System.Linq.Expressions.Expression.Lambda<Func<object, object, CancellationToken, Task>>(
            call, jobParam, argParam, ctParam).Compile();
    }
}
