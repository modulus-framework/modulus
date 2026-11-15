using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Sagas;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Sagas.Tests;

[Trait("Category", "Unit")]
public sealed class OrderSagaTests
{
    private const string SagaEndpoint = "orders-saga";

    [Fact]
    public async Task FR_17_the_happy_path_stores_the_instance_publishes_the_outcome_and_completes()
    {
        var orderId = Guid.NewGuid();
        await using var harness = await StartAsync(configure: null);
        var repository = harness.Services.GetRequiredService<ISagaRepository<OrderSaga>>();
        var observed = harness.Services.GetRequiredService<Observed>();

        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new OrderSubmitted(orderId, 42m));
        (await WaitForAsync(async () => (await repository.FindAsync(orderId, CancellationToken.None))?.CurrentState == "Submitted")).Should().BeTrue();
        var stored = await repository.FindAsync(orderId, CancellationToken.None);
        stored!.Amount.Should().Be(42m);

        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new PaymentReceived(orderId));

        (await WaitForAsync(() => observed.Messages.OfType<OrderAccepted>().Any(m => m.OrderId == orderId))).Should().BeTrue();
        (await WaitForAsync(async () => await repository.FindAsync(orderId, CancellationToken.None) is null)).Should().BeTrue("a completed saga is removed");
    }

    [Fact]
    public async Task FR_17_the_timeout_path_publishes_the_failure_when_no_payment_arrives()
    {
        var orderId = Guid.NewGuid();
        await using var harness = await StartAsync(configure: null);
        var repository = harness.Services.GetRequiredService<ISagaRepository<OrderSaga>>();
        var observed = harness.Services.GetRequiredService<Observed>();

        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new OrderSubmitted(orderId, 10m));

        (await WaitForAsync(() => observed.Messages.OfType<OrderFailed>().Any(m => m.OrderId == orderId))).Should().BeTrue("the schedule fired the timeout");
        (await WaitForAsync(async () => await repository.FindAsync(orderId, CancellationToken.None) is null)).Should().BeTrue();
        observed.Messages.OfType<OrderAccepted>().Should().BeEmpty();
    }

    [Fact]
    public async Task FR_17_the_cancel_path_completes_the_saga_and_publishes_nothing()
    {
        var orderId = Guid.NewGuid();
        await using var harness = await StartAsync(configure: null);
        var repository = harness.Services.GetRequiredService<ISagaRepository<OrderSaga>>();
        var observed = harness.Services.GetRequiredService<Observed>();

        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new OrderSubmitted(orderId, 10m));
        (await WaitForAsync(async () => await repository.FindAsync(orderId, CancellationToken.None) is not null)).Should().BeTrue();
        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new OrderCancelled(orderId));

        (await WaitForAsync(async () => await repository.FindAsync(orderId, CancellationToken.None) is null)).Should().BeTrue();

        // The timeout is still scheduled. It arrives after the saga completed, so it must publish nothing.
        await Task.Delay(OrderStateMachine.PaymentWindow + TimeSpan.FromMilliseconds(400));
        observed.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task FR_17_an_event_that_arrives_before_its_saga_is_redelivered_and_handled()
    {
        var orderId = Guid.NewGuid();
        await using var harness = await StartAsync(o => o.Redelivery = RedeliveryPolicy.Intervals(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(100)));
        var observed = harness.Services.GetRequiredService<Observed>();

        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new PaymentReceived(orderId));
        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new OrderSubmitted(orderId, 5m));

        (await WaitForAsync(() => observed.Messages.OfType<OrderAccepted>().Any(m => m.OrderId == orderId))).Should().BeTrue("the early payment was redelivered once the saga existed");
    }

    [Fact]
    public async Task FR_17_an_event_the_state_does_not_handle_leaves_the_saga_unchanged()
    {
        var orderId = Guid.NewGuid();
        await using var harness = await StartAsync(configure: null);
        var repository = harness.Services.GetRequiredService<ISagaRepository<OrderSaga>>();

        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new OrderSubmitted(orderId, 42m));
        (await WaitForAsync(async () => await repository.FindAsync(orderId, CancellationToken.None) is not null)).Should().BeTrue();

        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new OrderSubmitted(orderId, 99m));
        await Task.Delay(300);

        var stored = await repository.FindAsync(orderId, CancellationToken.None);
        stored!.Amount.Should().Be(42m, "the second submit is not handled in Submitted and changes nothing");
        stored.CurrentState.Should().Be("Submitted");
    }

    [Fact]
    public void A_machine_with_an_event_that_has_no_correlation_key_is_refused_at_start()
    {
        var builder = new MessagingBusBuilder();

        Action act = () => builder.AddSaga<UncorrelatedOrderStateMachine, OrderSaga>("uncorrelated");

        var error = act.Should().Throw<Exception>().Which;
        error.InnerException.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("CorrelateBy");
    }

    private static Task<MessagingTestHarness> StartAsync(Action<ConsumerOptions>? configure) =>
        MessagingTestHarness.StartAsync(
            b => b
                .AddSaga<OrderStateMachine, OrderSaga>(SagaEndpoint, configure)
                .AddConsumer<ObservedConsumer<OrderAccepted>, OrderAccepted>("probe-accepted")
                .AddConsumer<ObservedConsumer<OrderFailed>, OrderFailed>("probe-failed")
                .UseInMemory(),
            services => services
                .AddInMemorySagaRepository<OrderSaga>()
                .AddSingleton<Observed>());

    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }

    private static async Task<bool> WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return await condition();
    }
}
