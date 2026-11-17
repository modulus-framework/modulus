using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

/// <summary>Item 15: a scheduled publish that fails partway leaves no rows under its token.</summary>
[Trait("Category", "Unit")]
public sealed class OutboxScheduleRollbackTests
{
    /// <summary>Accepts the first write, fails the next, and records what was written and cancelled.</summary>
    private sealed class FailingSecondWriteOutbox : IMessageOutbox
    {
        public List<Guid> Written { get; } = [];

        public List<Guid> Cancelled { get; } = [];

        public Task Add(MessageEnvelope envelope, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task AddScheduled(MessageEnvelope envelope, DateTimeOffset deliverAt, Guid scheduleToken, CancellationToken cancellationToken)
        {
            if (Written.Count == 1)
            {
                throw new InvalidOperationException("the second write failed");
            }

            Written.Add(scheduleToken);
            return Task.CompletedTask;
        }

        public Task<bool> CancelScheduled(Guid scheduleToken, CancellationToken cancellationToken)
        {
            Cancelled.Add(scheduleToken);
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task A_publish_that_fails_on_its_second_row_cancels_the_first_and_rethrows()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<BillingConsumer, OrderSubmitted>("billing")
            .AddConsumer<ShippingConsumer, OrderSubmitted>("shipping")
            .UseInMemory(), services => services.AddSingleton<Sink>());
        var bus = harness.Services.GetRequiredService<MessagingBus>();
        var outbox = new FailingSecondWriteOutbox();
        var scheduler = new OutboxMessageScheduler(bus, outbox);

        Func<Task> act = () => scheduler.SchedulePublish(DateTimeOffset.UtcNow.AddHours(1), new OrderSubmitted(Guid.NewGuid()));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("the second write failed");
        outbox.Written.Should().HaveCount(1, "the first row was written before the failure");
        outbox.Cancelled.Should().ContainSingle().Which.Should().Be(outbox.Written[0], "the partial schedule is cancelled under its own token");
    }
}
