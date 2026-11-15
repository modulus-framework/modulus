using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

/// <summary>Counts consume attempts across the test's consumers.</summary>
public sealed class Attempts
{
    private int _value;

    public int Next() => Interlocked.Increment(ref _value);

    public int Value => Volatile.Read(ref _value);
}

public sealed class FaultLog
{
    public ConcurrentQueue<Fault<OrderSubmitted>> Items { get; } = new();
}

public sealed class FlakyConsumer(Attempts attempts, Sink sink) : IConsumer<OrderSubmitted>
{
    public Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        var attempt = attempts.Next();
        if (attempt < 3)
        {
            throw new InvalidOperationException($"transient failure {attempt}");
        }

        sink.Calls.Enqueue("flaky:ok");
        return Task.CompletedTask;
    }
}

public sealed class AlwaysFailingConsumer(Attempts attempts) : IConsumer<OrderSubmitted>
{
    public Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        attempts.Next();
        throw new InvalidOperationException("boom");
    }
}

public sealed class IgnoredFailureConsumer(Attempts attempts) : IConsumer<OrderSubmitted>
{
    public Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        attempts.Next();
        throw new ArgumentException("bad input");
    }
}

public sealed class SlowConsumer(Attempts attempts) : IConsumer<OrderSubmitted>
{
    public async Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        attempts.Next();
        await Task.Delay(TimeSpan.FromSeconds(5), context.CancellationToken);
    }
}

public sealed class DrainingConsumer(Attempts attempts, Sink sink) : IConsumer<OrderSubmitted>
{
    public async Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        attempts.Next();
        await Task.Delay(TimeSpan.FromMilliseconds(300), context.CancellationToken);
        sink.Calls.Enqueue("drained");
    }
}

public sealed class FaultRecorder(FaultLog log) : IConsumer<Fault<OrderSubmitted>>
{
    public Task Consume(ConsumeContext<Fault<OrderSubmitted>> context)
    {
        log.Items.Enqueue(context.Message);
        return Task.CompletedTask;
    }
}

[Trait("Category", "Unit")]
public sealed class RetryFaultAndDrainTests
{
    private static void RegisterTestServices(IServiceCollection services)
    {
        services.AddSingleton<Sink>();
        services.AddSingleton<Attempts>();
        services.AddSingleton<FaultLog>();
    }

    [Fact]
    public async Task FR_07_immediate_retry_succeeds_on_the_third_attempt()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<FlakyConsumer, OrderSubmitted>("billing", o => o.Retry = RetryPolicy.Immediate(3))
            .UseInMemory(), RegisterTestServices);

        await harness.Bus.Publish(new OrderSubmitted(Guid.NewGuid()));

        var sink = harness.Services.GetRequiredService<Sink>();
        var attempts = harness.Services.GetRequiredService<Attempts>();
        (await MessagingTestHarness.WaitUntilAsync(() => sink.Calls.Count == 1)).Should().BeTrue();
        attempts.Value.Should().Be(3);
        harness.Transport.Errors("billing").Should().BeEmpty();
    }

    [Fact]
    public async Task FR_07_an_ignored_exception_goes_straight_to_error_without_retry()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<IgnoredFailureConsumer, OrderSubmitted>("billing",
                o => o.Retry = RetryPolicy.Immediate(3).Ignore<ArgumentException>())
            .UseInMemory(), RegisterTestServices);

        await harness.Bus.Publish(new OrderSubmitted(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Errors("billing").Count == 1)).Should().BeTrue();
        harness.Services.GetRequiredService<Attempts>().Value.Should().Be(1);
    }

    [Fact]
    public async Task FR_07_once_retries_are_exhausted_the_message_is_parked_after_every_attempt()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<AlwaysFailingConsumer, OrderSubmitted>("billing", o => o.Retry = RetryPolicy.Immediate(2))
            .UseInMemory(), RegisterTestServices);

        await harness.Bus.Publish(new OrderSubmitted(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Errors("billing").Count == 1)).Should().BeTrue();
        harness.Services.GetRequiredService<Attempts>().Value.Should().Be(3);
    }

    [Fact]
    public async Task FR_08_redelivery_waits_each_delay_and_counts_redeliveries_then_parks()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<AlwaysFailingConsumer, OrderSubmitted>("billing",
                o => o.Redelivery = RedeliveryPolicy.Intervals(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20)))
            .UseInMemory(), RegisterTestServices);

        await harness.Bus.Publish(new OrderSubmitted(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Errors("billing").Count == 1)).Should().BeTrue();
        harness.Services.GetRequiredService<Attempts>().Value.Should().Be(3);
        harness.Transport.Errors("billing").Single().Envelope.Headers["mod-redelivery-count"].Should().Be("2");
    }

    [Fact]
    public async Task FR_05_a_parked_message_publishes_a_fault_that_a_fault_consumer_receives()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<AlwaysFailingConsumer, OrderSubmitted>("billing")
            .AddConsumer<FaultRecorder, Fault<OrderSubmitted>>("faults")
            .UseInMemory(), RegisterTestServices);

        var orderId = Guid.NewGuid();
        await harness.Bus.Publish(new OrderSubmitted(orderId));

        var log = harness.Services.GetRequiredService<FaultLog>();
        (await MessagingTestHarness.WaitUntilAsync(() => log.Items.Count == 1)).Should().BeTrue();
        var fault = log.Items.Single();
        fault.Endpoint.Should().Be("billing");
        fault.Message.Should().Be(new OrderSubmitted(orderId));
        fault.ExceptionMessage.Should().Be("boom");
        fault.ExceptionType.Should().Be(typeof(InvalidOperationException).FullName);
    }

    [Fact]
    public async Task FR_16_a_consumer_that_outlives_its_timeout_fails_and_is_parked()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<SlowConsumer, OrderSubmitted>("billing", o =>
            {
                o.ConsumeTimeout = TimeSpan.FromMilliseconds(50);
                o.Retry = RetryPolicy.Immediate(1);
            })
            .UseInMemory(), RegisterTestServices);

        await harness.Bus.Publish(new OrderSubmitted(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Errors("billing").Count == 1)).Should().BeTrue();
        harness.Services.GetRequiredService<Attempts>().Value.Should().Be(2);
        harness.Transport.Errors("billing").Single().Exception.Should().BeOfType<ConsumeTimeoutException>();
    }

    [Fact]
    public async Task FR_C1_a_consumer_setting_overrides_the_bus_default_for_that_consumer_only()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .ConfigureDefaults(o => o.Retry = RetryPolicy.Immediate(5))
            .AddConsumer<AlwaysFailingConsumer, OrderSubmitted>("inherits")
            .AddConsumer<AlwaysFailingConsumer, OrderSubmitted>("own", o => o.Retry = RetryPolicy.Immediate(1))
            .UseInMemory(), RegisterTestServices);

        await harness.Bus.Publish(new OrderSubmitted(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() =>
            harness.Transport.Errors("inherits").Count == 1 && harness.Transport.Errors("own").Count == 1)).Should().BeTrue();
        // 6 attempts for the endpoint that inherits 5 retries, 2 for the endpoint with its own 1 retry.
        harness.Services.GetRequiredService<Attempts>().Value.Should().Be(8);
    }

    [Fact]
    public async Task FR_16_stopping_lets_an_in_flight_message_finish()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<DrainingConsumer, OrderSubmitted>("billing")
            .UseInMemory(), RegisterTestServices);

        await harness.Bus.Publish(new OrderSubmitted(Guid.NewGuid()));
        var attempts = harness.Services.GetRequiredService<Attempts>();
        (await MessagingTestHarness.WaitUntilAsync(() => attempts.Value == 1)).Should().BeTrue();

        await harness.StopAsync();

        harness.Services.GetRequiredService<Sink>().Calls.Should().ContainSingle().Which.Should().Be("drained");
        harness.Transport.Errors("billing").Should().BeEmpty();
    }

    [Fact]
    public void FR_07_exponential_and_incremental_delays_are_computed_per_retry()
    {
        var exponential = RetryPolicy.Exponential(4, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(500));
        exponential.DelayFor(1).Should().Be(TimeSpan.FromMilliseconds(100));
        exponential.DelayFor(2).Should().Be(TimeSpan.FromMilliseconds(200));
        exponential.DelayFor(3).Should().Be(TimeSpan.FromMilliseconds(400));
        exponential.DelayFor(4).Should().Be(TimeSpan.FromMilliseconds(500));

        var incremental = RetryPolicy.Incremental(3, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(5));
        incremental.Retries.Should().Be(3);
        incremental.DelayFor(1).Should().Be(TimeSpan.FromMilliseconds(10));
        incremental.DelayFor(3).Should().Be(TimeSpan.FromMilliseconds(20));
    }
}
