using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.BackgroundJobs;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Regression coverage for H4: <see cref="ChannelJobQueue.StopAsync"/> used to
/// cancel its <see cref="CancellationTokenSource"/> BEFORE completing the
/// channel writer, so every worker's <c>ReadAllAsync(ct)</c> threw
/// immediately on its next loop iteration and any job still sitting in the
/// channel -- enqueued but not yet started -- was silently dropped, despite
/// a comment claiming a "bounded grace period to drain". StopAsync now
/// completes the writer first so ReadAllAsync finishes naturally once the
/// channel empties, and only cancels afterward to unblock anything still
/// stuck past the grace period.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ChannelJobQueueStopAsyncTests
{
    private sealed record TestArgs(int Id);

    private sealed class RecordingJob(ConcurrentBag<int> completed) : IBackgroundJob<TestArgs>
    {
        public async Task ExecuteAsync(TestArgs args, CancellationToken ct)
        {
            // Slow enough that, at the default worker-pool size, most of the
            // jobs enqueued below are still sitting in the channel by the
            // time the test calls StopAsync.
            await Task.Delay(15, ct);
            completed.Add(args.Id);
        }
    }

    [Fact]
    public async Task StopAsync_DrainsAlreadyQueuedJobs_InsteadOfDroppingThem()
    {
        var completed = new ConcurrentBag<int>();
        var services = new ServiceCollection();
        services.AddSingleton(completed);
        services.AddTransient<IBackgroundJob<TestArgs>>(
            sp => new RecordingJob(sp.GetRequiredService<ConcurrentBag<int>>()));
        var sp = services.BuildServiceProvider();

        var queue = new ChannelJobQueue(sp, NullLogger<ChannelJobQueue>.Instance);
        await queue.StartAsync(CancellationToken.None);

        const int jobCount = 200;
        for (var i = 0; i < jobCount; i++)
            await queue.EnqueueAsync<RecordingJob, TestArgs>(new TestArgs(i), CancellationToken.None);

        // No delay here on purpose: stopping immediately after enqueueing is
        // exactly the race that used to drop queued jobs.
        await queue.StopAsync(CancellationToken.None);

        completed.Should().HaveCount(jobCount,
            "every already-enqueued job must be drained before shutdown, not dropped mid-channel");
    }
}
