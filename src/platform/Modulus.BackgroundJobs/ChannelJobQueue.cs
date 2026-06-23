namespace Modulus.BackgroundJobs;

using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

internal sealed record JobEnvelope(
    Type   JobType,
    Type   ArgsType,
    object Args,
    TimeSpan Delay = default);

public sealed class ChannelJobQueue(
    IServiceProvider              sp,
    ILogger<ChannelJobQueue>      logger)
    : IJobScheduler, IHostedService
{
    private readonly Channel<JobEnvelope> _channel =
        Channel.CreateUnbounded<JobEnvelope>(
            new UnboundedChannelOptions
            { SingleReader = false, SingleWriter = false });

    private readonly Dictionary<string, (JobEnvelope envelope, string cron)>
        _recurring = [];

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
        => _recurring[jobId] =
            (new JobEnvelope(typeof(TJob), typeof(TArgs), args!), cronExpression);

    public void RemoveRecurring(string jobId)
        => _recurring.Remove(jobId);

    // ── IHostedService ────────────────────────────────────────────
    public Task StartAsync(CancellationToken ct)
    {
        var workerCount = Math.Max(1, Environment.ProcessorCount / 2);
        for (var i = 0; i < workerCount; i++)
            _ = Task.Run(() => ProcessLoopAsync(ct), ct);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _channel.Writer.Complete();
        return Task.CompletedTask;
    }

    private async Task ProcessLoopAsync(CancellationToken ct)
    {
        await foreach (var envelope in _channel.Reader.ReadAllAsync(ct))
        {
            await using var scope = sp.CreateAsyncScope();
            try
            {
                dynamic job = scope.ServiceProvider
                    .GetRequiredService(envelope.JobType);
                await job.ExecuteAsync((dynamic)envelope.Args, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job {Type} failed",
                    envelope.JobType.Name);
            }
        }
    }
}