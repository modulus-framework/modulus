using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

public sealed record BreakerJob(Guid Id);

/// <summary>Records each attempt's number and start time. The attempt succeeds when <see cref="Succeeds"/> says so.</summary>
public sealed class BreakerLog
{
    private int _attempts;

    public ConcurrentQueue<(int Attempt, long Ticks)> Attempts { get; } = new();

    public ConcurrentQueue<Guid> Handled { get; } = new();

    public Func<int, bool> Succeeds { get; set; } = _ => false;

    internal int Next() => Interlocked.Increment(ref _attempts);
}

public sealed class BreakerConsumer(BreakerLog log) : IConsumer<BreakerJob>
{
    public Task Consume(ConsumeContext<BreakerJob> context)
    {
        var attempt = log.Next();
        log.Attempts.Enqueue((attempt, Stopwatch.GetTimestamp()));
        if (!log.Succeeds(attempt))
        {
            throw new InvalidOperationException($"attempt {attempt} fails");
        }

        log.Handled.Enqueue(context.Message.Id);
        return Task.CompletedTask;
    }
}

[Trait("Category", "Unit")]
public sealed class CircuitBreakerTests
{
    [Fact]
    public async Task FR_15_the_circuit_opens_after_consecutive_failures_and_closes_when_the_trial_succeeds()
    {
        var endpoint = $"breaker-{Guid.NewGuid():N}";
        var open = TimeSpan.FromMilliseconds(400);
        var log = new BreakerLog { Succeeds = attempt => attempt >= 4 };
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<BreakerConsumer, BreakerJob>(endpoint, o => o.CircuitBreaker = new CircuitBreakerOptions { FailureThreshold = 3, OpenDuration = open })
            .UseInMemory(), services => services.AddSingleton(log));

        for (var i = 0; i < 3; i++)
        {
            await harness.Bus.Send(new Uri($"queue:{endpoint}"), new BreakerJob(Guid.NewGuid()));
        }

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Errors(endpoint).Count == 3)).Should().BeTrue();
        var fourth = Guid.NewGuid();
        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new BreakerJob(fourth));
        var fifth = Guid.NewGuid();
        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new BreakerJob(fifth));

        (await MessagingTestHarness.WaitUntilAsync(() => log.Handled.Contains(fifth))).Should().BeTrue();
        log.Handled.Should().Contain(fourth, "the trial message succeeded");
        var attempts = log.Attempts.ToArray();
        var lastFailure = attempts.Single(a => a.Attempt == 3).Ticks;
        var trial = attempts.Single(a => a.Attempt == 4).Ticks;
        Stopwatch.GetElapsedTime(lastFailure, trial).Should().BeGreaterThanOrEqualTo(open - TimeSpan.FromMilliseconds(60),
            "no message starts while the circuit is open");
    }

    [Fact]
    public async Task FR_15_a_failed_trial_opens_the_circuit_again()
    {
        var endpoint = $"breaker-{Guid.NewGuid():N}";
        var open = TimeSpan.FromMilliseconds(400);
        var log = new BreakerLog { Succeeds = _ => false };
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<BreakerConsumer, BreakerJob>(endpoint, o => o.CircuitBreaker = new CircuitBreakerOptions { FailureThreshold = 2, OpenDuration = open })
            .UseInMemory(), services => services.AddSingleton(log));

        for (var i = 0; i < 2; i++)
        {
            await harness.Bus.Send(new Uri($"queue:{endpoint}"), new BreakerJob(Guid.NewGuid()));
        }

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Errors(endpoint).Count == 2)).Should().BeTrue();
        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new BreakerJob(Guid.NewGuid()));
        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new BreakerJob(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() => log.Attempts.Count >= 4)).Should().BeTrue();
        var attempts = log.Attempts.ToArray();
        var trial = attempts.Single(a => a.Attempt == 3).Ticks;
        var next = attempts.Single(a => a.Attempt == 4).Ticks;
        Stopwatch.GetElapsedTime(trial, next).Should().BeGreaterThanOrEqualTo(open - TimeSpan.FromMilliseconds(60),
            "the failed trial opens the circuit for another open duration");
    }

    [Fact]
    public void FR_15_an_open_duration_of_zero_is_refused()
    {
        var options = new ConsumerOptions { CircuitBreaker = new CircuitBreakerOptions { OpenDuration = TimeSpan.Zero } };

        var act = () => options.Validate();

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("OpenDuration");
    }
}
