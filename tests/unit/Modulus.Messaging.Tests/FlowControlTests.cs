using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

/// <summary>Records when each consume attempt started, in milliseconds since the test's stopwatch began.</summary>
public sealed class Timeline
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public ConcurrentQueue<(int Attempt, long Milliseconds)> Starts { get; } = new();

    public void Record(int attempt) => Starts.Enqueue((attempt, _clock.ElapsedMilliseconds));
}

public sealed class TimedConsumer(Attempts attempts, Timeline timeline) : IConsumer<OrderSubmitted>
{
    public Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        timeline.Record(attempts.Next());
        return Task.CompletedTask;
    }
}

/// <summary>Fails its first four attempts, then succeeds.</summary>
public sealed class FailFirstFourConsumer(Attempts attempts, Timeline timeline) : IConsumer<OrderSubmitted>
{
    public Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        var attempt = attempts.Next();
        timeline.Record(attempt);
        if (attempt <= 4)
        {
            throw new InvalidOperationException($"failure {attempt}");
        }

        return Task.CompletedTask;
    }
}

[Trait("Category", "Unit")]
public sealed class FlowControlTests
{
    private static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<Attempts>();
        services.AddSingleton<Timeline>();
    }

    [Fact]
    public async Task FR_15_the_rate_limit_spaces_deliveries_to_the_endpoint()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<TimedConsumer, OrderSubmitted>("billing", o => o.RateLimit = new RateLimit(2, TimeSpan.FromMilliseconds(300)))
            .UseInMemory(), RegisterServices);

        for (var i = 0; i < 4; i++)
        {
            await harness.Bus.Publish(new OrderSubmitted(Guid.NewGuid()));
        }

        var timeline = harness.Services.GetRequiredService<Timeline>();
        (await MessagingTestHarness.WaitUntilAsync(() => timeline.Starts.Count == 4, TimeSpan.FromSeconds(5))).Should().BeTrue();
        var times = timeline.Starts.Select(s => s.Milliseconds).ToArray();
        // The first two go at once; the third waits for the window to slide, so the first and last are a period apart.
        (times[3] - times[0]).Should().BeGreaterThanOrEqualTo(250);
    }

    [Fact]
    public async Task FR_15_the_kill_switch_pauses_the_endpoint_and_restarts_it_after_its_timeout()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<FailFirstFourConsumer, OrderSubmitted>("billing", o => o.KillSwitch = new KillSwitchOptions
            {
                FailureRatio = 1.0,
                WindowSize = 4,
                RestartTimeout = TimeSpan.FromMilliseconds(400),
            })
            .UseInMemory(), RegisterServices);

        for (var i = 0; i < 5; i++)
        {
            await harness.Bus.Publish(new OrderSubmitted(Guid.NewGuid()));
        }

        var timeline = harness.Services.GetRequiredService<Timeline>();
        (await MessagingTestHarness.WaitUntilAsync(() => timeline.Starts.Count == 5, TimeSpan.FromSeconds(5))).Should().BeTrue();
        harness.Transport.Errors("billing").Count.Should().Be(4);

        var starts = timeline.Starts.ToArray();
        // Attempt 5 waits out the pause that the fourth consecutive failure started.
        (starts[4].Milliseconds - starts[3].Milliseconds).Should().BeGreaterThanOrEqualTo(350);
    }
}
