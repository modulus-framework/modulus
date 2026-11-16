using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

/// <summary>FR-10 (in-memory): a scope's messages go out only when the scope commits; a rollback sends nothing.</summary>
[Trait("Category", "Unit")]
public sealed class InMemoryOutboxTests
{
    public sealed class CountingConsumer(Attempts attempts) : IConsumer<OrderSubmitted>
    {
        public Task Consume(ConsumeContext<OrderSubmitted> context)
        {
            attempts.Next();
            return Task.CompletedTask;
        }
    }

    private static async Task<MessagingTestHarness> StartAsync() =>
        await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<CountingConsumer, OrderSubmitted>("billing-outbox")
            .UseInMemory(), services =>
            {
                services.AddSingleton<Attempts>();
                services.AddModulusMessagingInMemoryOutbox();
            });

    [Fact]
    public async Task UC_01_a_scope_that_never_commits_sends_nothing()
    {
        await using var harness = await StartAsync();

        using (var scope = harness.Services.CreateScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IBus>();
            await bus.Publish(new OrderSubmitted(Guid.NewGuid()));
            scope.ServiceProvider.GetRequiredService<IInMemoryMessageOutbox>().Pending.Should().Be(1);
        }

        await Task.Delay(150);
        harness.Services.GetRequiredService<Attempts>().Value.Should().Be(0, "the scope ended without a commit");
    }

    [Fact]
    public async Task UC_01_a_commit_sends_each_message_once()
    {
        await using var harness = await StartAsync();

        using var scope = harness.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IBus>();
        var outbox = scope.ServiceProvider.GetRequiredService<IInMemoryMessageOutbox>();
        await bus.Publish(new OrderSubmitted(Guid.NewGuid()));
        await bus.Send(new Uri("queue:billing-outbox"), new OrderSubmitted(Guid.NewGuid()));
        harness.Services.GetRequiredService<Attempts>().Value.Should().Be(0, "nothing is sent before the commit");

        await outbox.CommitAsync();

        var attempts = harness.Services.GetRequiredService<Attempts>();
        (await MessagingTestHarness.WaitUntilAsync(() => attempts.Value == 2)).Should().BeTrue();
        outbox.Pending.Should().Be(0);

        await outbox.CommitAsync();
        await Task.Delay(150);
        attempts.Value.Should().Be(2, "a second commit has nothing left to send");
    }

    [Fact]
    public async Task UC_01_a_rollback_discards_the_pending_messages()
    {
        await using var harness = await StartAsync();

        using var scope = harness.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IBus>();
        var outbox = scope.ServiceProvider.GetRequiredService<IInMemoryMessageOutbox>();
        await bus.Publish(new OrderSubmitted(Guid.NewGuid()));

        outbox.Rollback();
        await outbox.CommitAsync();

        await Task.Delay(150);
        harness.Services.GetRequiredService<Attempts>().Value.Should().Be(0);
        outbox.Pending.Should().Be(0);
    }

    [Fact]
    public async Task Scheduling_inside_the_in_memory_outbox_is_refused_not_faked()
    {
        await using var harness = await StartAsync();

        using var scope = harness.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IMessageScheduler>();

        var act = () => scheduler.ScheduleSend(new Uri("queue:billing-outbox"), DateTimeOffset.UtcNow.AddMinutes(1), new OrderSubmitted(Guid.NewGuid()));

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
