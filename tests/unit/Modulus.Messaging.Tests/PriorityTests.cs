using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

[Trait("Category", "Unit")]
public sealed class PriorityTests
{
    [Fact]
    public async Task FR_13_on_the_in_memory_transport_the_highest_priority_is_consumed_first()
    {
        var log = new ConcurrentQueue<string>();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddModulusMessaging(b => b
            .AddConsumer<PriorityLogConsumer, PriorityJob>("jobs")
            .AddConsumer<PriorityLogConsumer, UrgentPriorityJob>("jobs")
            .UseInMemory());
        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBus>();

        // Queue everything before the host starts, so the order is decided by the queue alone.
        await bus.Send(new Uri("queue:jobs"), new PriorityJob("low-1"));
        await bus.Send(new Uri("queue:jobs"), new PriorityJob("low-2"));
        await bus.Send(new Uri("queue:jobs"), new UrgentPriorityJob("urgent"));
        await bus.Send(new Uri("queue:jobs"), new PriorityJob("low-3"));
        await bus.Send(new Uri("queue:jobs"), new UrgentPriorityJob("urgent-2"));

        await provider.GetServices<IHostedService>().Single().StartAsync(CancellationToken.None);

        (await MessagingTestHarness.WaitUntilAsync(() => log.Count == 5)).Should().BeTrue();
        log.Should().Equal("urgent", "urgent-2", "low-1", "low-2", "low-3");
    }
}

public sealed record PriorityJob(string Name);

[MessagePriority(10)]
public sealed record UrgentPriorityJob(string Name);

public sealed class PriorityLogConsumer(ConcurrentQueue<string> log) : IConsumer<PriorityJob>, IConsumer<UrgentPriorityJob>
{
    public Task Consume(ConsumeContext<PriorityJob> context)
    {
        log.Enqueue(context.Message.Name);
        return Task.CompletedTask;
    }

    Task IConsumer<UrgentPriorityJob>.Consume(ConsumeContext<UrgentPriorityJob> context)
    {
        log.Enqueue(context.Message.Name);
        return Task.CompletedTask;
    }
}
