using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.BackgroundJobs;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>A job that fails once runs again after the back-off instead of being dropped.</summary>
[Trait("Category", "Unit")]
public sealed class ChannelJobQueueRetryTests
{
    private sealed record Args(int Id);

    private sealed class FlakyJob(ConcurrentQueue<int> attempts) : IBackgroundJob<Args>
    {
        public Task ExecuteAsync(Args args, CancellationToken ct)
        {
            var attempt = attempts.Count + 1;
            attempts.Enqueue(attempt);
            if (attempt == 1)
                throw new InvalidOperationException("a transient outage");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_failed_job_runs_again_after_a_back_off()
    {
        var attempts = new ConcurrentQueue<int>();
        var services = new ServiceCollection();
        services.AddSingleton(attempts);
        services.AddTransient<IBackgroundJob<Args>>(sp => new FlakyJob(sp.GetRequiredService<ConcurrentQueue<int>>()));
        var queue = new ChannelJobQueue(services.BuildServiceProvider(), NullLogger<ChannelJobQueue>.Instance);
        await queue.StartAsync(CancellationToken.None);

        await queue.EnqueueAsync<FlakyJob, Args>(new Args(1), CancellationToken.None);

        // The first back-off is five seconds, so the wait is bounded by the retry, not by a fixed sleep.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (attempts.Count < 2 && DateTime.UtcNow < deadline)
            await Task.Delay(100);
        await queue.StopAsync(CancellationToken.None);

        attempts.ToArray().Should().Equal(1, 2);
    }

    [Fact]
    public void Retries_are_bounded()
    {
        ChannelJobQueue.MaxAttempts.Should().Be(3, "a job that keeps failing is eventually dropped, not retried forever");
    }
}
