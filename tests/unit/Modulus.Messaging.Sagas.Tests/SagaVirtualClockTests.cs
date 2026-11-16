using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Sagas.Tests;

[Trait("Category", "Unit")]
public sealed class SagaVirtualClockTests
{
    [Fact]
    public async Task FR_25_a_saga_timeout_fires_when_the_virtual_clock_passes_its_window_and_the_saga_completes()
    {
        var clock = new VirtualClock();
        var orderId = Guid.NewGuid();
        await using var harness = await MessagingTestHarness.StartAsync(
            b => b
                .AddSaga<OrderStateMachine, OrderSaga>("orders-saga")
                .AddConsumer<ObservedConsumer<OrderFailed>, OrderFailed>("probe-failed")
                .UseInMemory(),
            services => services
                .AddInMemorySagaRepository<OrderSaga>()
                .AddSingleton<Observed>(),
            clock);
        var sagas = new SagaHarness<OrderSaga>(harness);
        var observed = harness.Services.GetRequiredService<Observed>();

        await harness.Bus.Send(new Uri("queue:orders-saga"), new OrderSubmitted(orderId, 10m));
        (await sagas.WaitForStateAsync(orderId, "Submitted")).Should().BeTrue();

        // The payment window is two seconds of virtual time. Advance until its timeout has been published.
        for (var step = 0; step < 500 && !observed.Messages.OfType<OrderFailed>().Any(); step++)
        {
            clock.Advance(TimeSpan.FromSeconds(3));
            await Task.Delay(10);
        }

        observed.Messages.OfType<OrderFailed>().Should().ContainSingle(m => m.OrderId == orderId);
        (await sagas.WaitForCompletionAsync(orderId)).Should().BeTrue("the timeout completes the saga");
        (await sagas.FindAsync(orderId)).Should().BeNull();
    }
}
