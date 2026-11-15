using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

[Trait("Category", "Unit")]
public sealed class OrderingTests
{
    [Fact]
    public async Task FR_14_messages_with_the_same_partition_key_are_handled_in_send_order()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<KeyedEventConsumer, KeyedEvent>("ordered", o => o.Ordered = true)
            .UseInMemory(), services => services.AddSingleton<ConcurrentQueue<KeyedEvent>>());
        var log = harness.Services.GetRequiredService<ConcurrentQueue<KeyedEvent>>();

        // Interleave three partitions; the order within each must survive.
        for (var i = 0; i < 100; i++)
        {
            foreach (var customer in new[] { "a", "b", "c" })
            {
                await harness.Bus.Send(new Uri("queue:ordered"), new KeyedEvent(customer, i));
            }
        }

        (await MessagingTestHarness.WaitUntilAsync(() => log.Count == 300, TimeSpan.FromSeconds(10))).Should().BeTrue();
        foreach (var customer in new[] { "a", "b", "c" })
        {
            log.Where(e => e.CustomerId == customer).Select(e => e.Sequence)
                .Should().Equal(Enumerable.Range(0, 100), $"partition {customer} keeps its send order");
        }
    }

    [Fact]
    public async Task FR_14_an_ordered_consumer_without_a_key_uses_its_endpoint_as_one_partition()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<HeaderCaptureConsumer, PlainEvent>("ordered-plain", o => o.Ordered = true)
            .UseInMemory(), services => services.AddSingleton<ConcurrentQueue<IReadOnlyDictionary<string, string>>>());
        var captured = harness.Services.GetRequiredService<ConcurrentQueue<IReadOnlyDictionary<string, string>>>();

        await harness.Bus.Send(new Uri("queue:ordered-plain"), new PlainEvent());

        (await MessagingTestHarness.WaitUntilAsync(() => captured.Count == 1)).Should().BeTrue();
        captured.Single()[MessagingHeaders.PartitionKey].Should().Be("ordered-plain");
    }

    [Fact]
    public async Task FR_14_an_ordered_consumer_ignores_message_priority()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<HeaderCaptureConsumer, UrgentPlainEvent>("ordered-urgent", o => o.Ordered = true)
            .UseInMemory(), services => services.AddSingleton<ConcurrentQueue<IReadOnlyDictionary<string, string>>>());
        var captured = harness.Services.GetRequiredService<ConcurrentQueue<IReadOnlyDictionary<string, string>>>();

        await harness.Bus.Send(new Uri("queue:ordered-urgent"), new UrgentPlainEvent());

        (await MessagingTestHarness.WaitUntilAsync(() => captured.Count == 1)).Should().BeTrue();
        captured.Single().Should().NotContainKey(MessagingHeaders.Priority,
            "a higher priority would let a later message of the same partition overtake this one");
    }
}

public sealed record KeyedEvent([property: PartitionKey] string CustomerId, int Sequence);

public sealed class KeyedEventConsumer(ConcurrentQueue<KeyedEvent> log) : IConsumer<KeyedEvent>
{
    public Task Consume(ConsumeContext<KeyedEvent> context)
    {
        log.Enqueue(context.Message);
        return Task.CompletedTask;
    }
}

public sealed record PlainEvent;

[MessagePriority(10)]
public sealed record UrgentPlainEvent;

public sealed class HeaderCaptureConsumer(ConcurrentQueue<IReadOnlyDictionary<string, string>> captured) : IConsumer<PlainEvent>, IConsumer<UrgentPlainEvent>
{
    public Task Consume(ConsumeContext<PlainEvent> context)
    {
        captured.Enqueue(context.Headers);
        return Task.CompletedTask;
    }

    Task IConsumer<UrgentPlainEvent>.Consume(ConsumeContext<UrgentPlainEvent> context)
    {
        captured.Enqueue(context.Headers);
        return Task.CompletedTask;
    }
}
