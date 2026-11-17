using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

/// <summary>FR-C3: with nothing configured, retry, redelivery and the outbox are off; errors and faults are on.</summary>
[Trait("Category", "Unit")]
public sealed class DefaultsTests
{
    /// <summary>Counts every attempt, so a retry or redelivery would show up as a second call.</summary>
    public sealed class AttemptCounter
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public void Record() => Interlocked.Increment(ref _attempts);
    }

    public sealed class CountingFailingConsumer(AttemptCounter counter) : IConsumer<OrderSubmitted>
    {
        public Task Consume(ConsumeContext<OrderSubmitted> context)
        {
            counter.Record();
            throw new InvalidOperationException("boom");
        }
    }

    [Fact]
    public void Retry_redelivery_and_timeout_are_unset_on_a_new_consumer()
    {
        var options = new ConsumerOptions();

        options.Retry.Should().BeNull();
        options.Redelivery.Should().BeNull();
        options.ConsumeTimeout.Should().BeNull();
        options.KillSwitch.Should().BeNull();
        options.CircuitBreaker.Should().BeNull();
    }

    [Fact]
    public async Task A_failing_consumer_runs_once_then_goes_to_error_with_a_fault_and_no_retry_is_configured()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<CountingFailingConsumer, OrderSubmitted>("billing")
            .UseInMemory(),
            services => services.AddSingleton<AttemptCounter>());
        var counter = harness.Services.GetRequiredService<AttemptCounter>();

        await harness.Bus.Publish(new OrderSubmitted(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Errors("billing").Count == 1)).Should().BeTrue();
        await Task.Delay(200);
        counter.Attempts.Should().Be(1, "no retry or redelivery is configured, so the consumer runs once");
        harness.Transport.Errors("billing").Should().ContainSingle();
    }

    [Fact]
    public async Task No_outbox_is_registered_unless_the_application_adds_one()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<CountingFailingConsumer, OrderSubmitted>("billing")
            .UseInMemory(),
            services => services.AddSingleton<AttemptCounter>());

        harness.Services.GetService<IMessageOutbox>().Should().BeNull("the outbox is opt-in (FR-C3)");
        harness.Services.GetService<IMessageInbox>().Should().BeNull("the inbox is opt-in (FR-C3)");
    }
}
