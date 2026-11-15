using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

[Trait("Category", "Unit")]
public sealed class ConcurrencyTests
{
    [Fact]
    public async Task FR_14_ordered_messages_stay_in_send_order_under_concurrency_16()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<SlowKeyedEventConsumer, KeyedEvent>("ordered-16", o =>
            {
                o.Ordered = true;
                o.ConcurrentMessageLimit = 16;
            })
            .UseInMemory(), services => services
            .AddSingleton<ConcurrentQueue<KeyedEvent>>()
            .AddSingleton<ConcurrencyProbe>());
        var log = harness.Services.GetRequiredService<ConcurrentQueue<KeyedEvent>>();
        var probe = harness.Services.GetRequiredService<ConcurrencyProbe>();

        for (var i = 0; i < 100; i++)
        {
            foreach (var customer in new[] { "a", "b", "c", "d", "e" })
            {
                await harness.Bus.Send(new Uri("queue:ordered-16"), new KeyedEvent(customer, i));
            }
        }

        (await MessagingTestHarness.WaitUntilAsync(() => log.Count == 500, TimeSpan.FromSeconds(20))).Should().BeTrue();
        foreach (var customer in new[] { "a", "b", "c", "d", "e" })
        {
            log.Where(e => e.CustomerId == customer).Select(e => e.Sequence)
                .Should().Equal(Enumerable.Range(0, 100), $"partition {customer} keeps its send order");
        }

        probe.PeakInFlight.Should().BeGreaterThan(1, "different partitions run at the same time");
        probe.PeakInFlight.Should().BeLessOrEqualTo(16);
    }

    [Fact]
    public async Task FR_14_unordered_messages_run_up_to_the_concurrency_limit()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<SlowPlainEventConsumer, PlainEvent>("concurrent-16", o => o.ConcurrentMessageLimit = 16)
            .UseInMemory(), services => services.AddSingleton<ConcurrencyProbe>());
        var probe = harness.Services.GetRequiredService<ConcurrencyProbe>();

        for (var i = 0; i < 64; i++)
        {
            await harness.Bus.Send(new Uri("queue:concurrent-16"), new PlainEvent());
        }

        (await MessagingTestHarness.WaitUntilAsync(() => probe.Completed == 64, TimeSpan.FromSeconds(20))).Should().BeTrue();
        probe.PeakInFlight.Should().BeGreaterThan(1);
        probe.PeakInFlight.Should().BeLessOrEqualTo(16);
    }

    [Fact]
    public async Task Without_concurrency_support_a_higher_limit_is_refused_at_start()
    {
        // The harness forces the in-memory transport, so this builds the bus directly over a sequential-only transport.
        var services = new ServiceCollection();
        services.AddSingleton<ConcurrencyProbe>();
        services.AddModulusMessaging(b => b
            .AddConsumer<SlowPlainEventConsumer, PlainEvent>("refused", o => o.ConcurrentMessageLimit = 4)
            .UseTransport(_ => new SequentialOnlyTransport()));
        var host = services.BuildServiceProvider().GetServices<IHostedService>().Single();

        var act = () => host.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ConcurrentMessageLimit*");
    }
}

/// <summary>Tracks how many handlers run at once and how many finished.</summary>
public sealed class ConcurrencyProbe
{
    private int _inFlight;
    private int _peak;
    private int _completed;

    public int PeakInFlight => Volatile.Read(ref _peak);

    public int Completed => Volatile.Read(ref _completed);

    public async Task Run(Func<Task> body)
    {
        var now = Interlocked.Increment(ref _inFlight);
        InterlockedMax(ref _peak, now);
        try
        {
            await body().ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
            Interlocked.Increment(ref _completed);
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        do
        {
            seen = Volatile.Read(ref target);
            if (value <= seen)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, seen) != seen);
    }
}

public sealed class SlowKeyedEventConsumer(ConcurrentQueue<KeyedEvent> log, ConcurrencyProbe probe) : IConsumer<KeyedEvent>
{
    public Task Consume(ConsumeContext<KeyedEvent> context) => probe.Run(async () =>
    {
        await Task.Delay(Random.Shared.Next(1, 4)).ConfigureAwait(false);
        log.Enqueue(context.Message);
    });
}

public sealed class SlowPlainEventConsumer(ConcurrencyProbe probe) : IConsumer<PlainEvent>
{
    public Task Consume(ConsumeContext<PlainEvent> context) => probe.Run(() => Task.Delay(20));
}

/// <summary>A transport that delivers one message at a time per endpoint (it does not implement <see cref="IConcurrentDeliveryTransport"/>).</summary>
public sealed class SequentialOnlyTransport : IMessageTransport
{
    public Task Start(IReadOnlyCollection<string> endpoints, Func<MessageEnvelope, CancellationToken, Task> receive, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Stop(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Send(MessageEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Redeliver(MessageEnvelope envelope, TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task MoveToError(MessageEnvelope envelope, Exception exception, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task MoveToSkipped(MessageEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;
}
