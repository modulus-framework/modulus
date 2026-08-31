namespace Modulus.BackgroundJobs;

using System.Threading.Channels;
using Cronos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Core.Abstractions;
using Modulus.Core.Correlation;
using Modulus.Observability;

// Ambient context captured at enqueue and restored on the worker, so tenant
// query filters and log correlation behave inside the job as they did on the
// enqueuing flow (mirrors EnvelopeAmbientScope for broker messages).
internal sealed record JobEnvelope(
    Type JobType,
    Type ArgsType,
    object Args,
    TimeSpan Delay = default,
    Guid? TenantId = null,
    string? CorrelationId = null);

internal sealed record RecurringEntry(
    JobEnvelope Envelope,
    CronExpression Cron,
    DateTime NextRun);

/// <summary>
/// In-memory background job queue backed by <see cref="Channel{T}"/>.
/// Enqueued jobs run on a worker pool, and recurring jobs fire on a 30-second
/// scheduler — but all state (enqueued jobs, delayed jobs, cron schedules) is
/// held in memory and lost on process shutdown.
/// <para>
/// <b>Development use only.</b> For production, register a durable scheduler
/// (Quartz.NET, Hangfire) that persists jobs to a database and coordinates
/// execution across replicas. See <see cref="IJobScheduler"/> for the durability
/// boundary and why multi-replica deployments should not use this.
/// </para>
/// </summary>
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
            typeof(TJob), typeof(TArgs), args!,
            TenantId: ResolveTenantId(),
            CorrelationId: ResolveCorrelationId());
        await _channel.Writer.WriteAsync(envelope, ct);
    }

    public Task ScheduleAsync<TJob, TArgs>(
        TArgs args, TimeSpan delay, CancellationToken ct)
        where TJob : IBackgroundJob<TArgs>
    {
        var envelope = new JobEnvelope(
            typeof(TJob), typeof(TArgs), args!,
            TenantId: ResolveTenantId(),
            CorrelationId: ResolveCorrelationId());

        // Fire-and-forget: cancellation is expected (shutdown / caller abort)
        // and must not surface as an unobserved task exception; any other
        // fault is logged so a lost delayed enqueue is diagnosable.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, ct);
                await _channel.Writer.WriteAsync(envelope, ct);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to enqueue delayed job {JobType} after {Delay}",
                    typeof(TJob).Name, delay);
            }
        }, ct);
        return Task.CompletedTask;
    }

    public void AddRecurring<TJob, TArgs>(
        string jobId, string cronExpression, TArgs args)
        where TJob : IBackgroundJob<TArgs>
    {
        var envelope = new JobEnvelope(
            typeof(TJob), typeof(TArgs), args!,
            TenantId: ResolveTenantId(),
            CorrelationId: ResolveCorrelationId());
        var cron = CronExpression.Parse(cronExpression);
        var nextRun = cron.GetNextOccurrence(
            DateTimeOffset.UtcNow, TimeZoneInfo.Utc);

        lock (_recurringLock)
        {
            _recurring[jobId] = new RecurringEntry(
                envelope, cron, nextRun?.UtcDateTime ?? DateTime.UtcNow);
        }

        ModulusMeters.RecurringJobCount.Add(1);
        logger.LogInformation(
            "Recurring job {JobId} scheduled with '{Cron}', next run at {NextRun:O}",
            jobId, cronExpression, nextRun);
    }

    public void RemoveRecurring(string jobId)
    {
        lock (_recurringLock)
        {
            if (_recurring.Remove(jobId))
                ModulusMeters.RecurringJobCount.Add(-1);
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
            ModulusMeters.JobsStarted.Add(1);

            // Restore the ambient tenant/correlation captured at enqueue.
            // The accessors are stateless AsyncLocal wrappers, so resolving
            // them here (outside the job's scope) sets the flow the job then
            // runs on. TenantSlug is not carried across the boundary — jobs
            // that need it should re-resolve from the store.
            using var tenantScope = envelope.TenantId is { } tenantId
                && sp.GetService<ICurrentTenant>() is { } tenant
                    ? tenant.Change(new TenantInfo(tenantId, string.Empty))
                    : null;
            using var correlationScope = envelope.CorrelationId is { } correlationId
                && sp.GetService<ICorrelationContext>() is { } correlation
                    ? correlation.BeginScope(correlationId)
                    : null;

            try
            {
                // Resolve via the closed generic IBackgroundJob<TArgs> interface
                // so we can dispatch through the interface without dynamic/DLR.
                var jobInterface = typeof(IBackgroundJob<>).MakeGenericType(envelope.ArgsType);
                var job = scope.ServiceProvider.GetRequiredService(jobInterface);
                var invoker = s_jobInvokers.GetOrAdd(envelope.ArgsType, CompileJobInvoker);
                await invoker(job, envelope.Args, ct);
                ModulusMeters.JobsCompleted.Add(1);
            }
            catch (Exception ex)
            {
                ModulusMeters.JobsFailed.Add(1);
                logger.LogError(ex, "Job {Type} failed",
                    envelope.JobType.Name);
            }
        }
    }

    private Guid? ResolveTenantId()
        => sp.GetService<ICurrentTenant>() is { IsHost: false, TenantId: { } tenantId }
            ? tenantId
            : null;

    private string? ResolveCorrelationId()
        => sp.GetService<ICorrelationContext>()?.CorrelationId;

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
