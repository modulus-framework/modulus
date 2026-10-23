using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.BackgroundJobs;
using Modulus.BackgroundJobs.Extensions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// The in-memory recurring path (the scheduler fires on a tick, here 20 ms): a due recurring job runs once, as the system named by
/// its id, and waits for its next cron time instead of running again on every tick.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RecurringJobTests
{
    private sealed record TestArgs(int Id);

    private sealed class ActorJob(JobContext context, ConcurrentBag<string> seen) : IBackgroundJob<TestArgs>
    {
        public Task ExecuteAsync(TestArgs args, CancellationToken ct)
        {
            seen.Add(context.Actor ?? "none");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_due_recurring_job_runs_once_as_the_system_and_waits_for_its_next_cron_time()
    {
        var seen = new ConcurrentBag<string>();
        var services = new ServiceCollection();
        services.AddSingleton(seen);
        services.AddBackgroundJobs();
        services.AddTransient<IBackgroundJob<TestArgs>, ActorJob>();
        var queue = new ChannelJobQueue(services.BuildServiceProvider(), NullLogger<ChannelJobQueue>.Instance, TimeSpan.FromMilliseconds(20));

        await queue.StartAsync(CancellationToken.None);
        queue.AddRecurring<ActorJob, TestArgs>("nightly", "0 0 1 1 *", new TestArgs(7), DateTime.UtcNow.AddSeconds(-1));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (seen.IsEmpty && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        await Task.Delay(200); // about ten more ticks: the next run is a year away, so none may run
        await queue.StopAsync(CancellationToken.None);

        seen.Should().ContainSingle().Which.Should().Be("system:nightly");
    }
}
