using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Sagas;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Sagas.Tests;

[Trait("Category", "Unit")]
public sealed class SagaBranchingTests
{
    [Fact]
    public async Task D5_an_if_block_runs_when_its_condition_holds()
    {
        var endpoint = $"branching-{Guid.NewGuid():N}";
        var orderId = Guid.NewGuid();
        await using var harness = await StartAsync(endpoint);
        var repository = harness.Services.GetRequiredService<ISagaRepository<OrderSaga>>();
        var observed = harness.Services.GetRequiredService<Observed>();

        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new OrderSubmitted(orderId, 150m));
        (await WaitForAsync(async () => await repository.FindAsync(orderId, CancellationToken.None) is not null)).Should().BeTrue();
        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new PaymentReceived(orderId));

        (await WaitForAsync(() => observed.Messages.OfType<OrderAccepted>().Any(m => m.OrderId == orderId))).Should().BeTrue();
        (await WaitForAsync(async () => await repository.FindAsync(orderId, CancellationToken.None) is null)).Should().BeTrue("the block completed the saga");
    }

    [Fact]
    public async Task D5_an_if_block_is_skipped_when_its_condition_fails_and_the_event_is_accepted()
    {
        var endpoint = $"branching-{Guid.NewGuid():N}";
        var orderId = Guid.NewGuid();
        await using var harness = await StartAsync(endpoint);
        var repository = harness.Services.GetRequiredService<ISagaRepository<OrderSaga>>();
        var observed = harness.Services.GetRequiredService<Observed>();

        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new OrderSubmitted(orderId, 10m));
        (await WaitForAsync(async () => await repository.FindAsync(orderId, CancellationToken.None) is not null)).Should().BeTrue();
        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new PaymentReceived(orderId));
        await Task.Delay(300);

        observed.Messages.OfType<OrderAccepted>().Should().BeEmpty("the condition is false, so the block did not run");
        var stored = await repository.FindAsync(orderId, CancellationToken.None);
        stored!.CurrentState.Should().Be("Submitted", "the saga is unchanged");
        stored.Amount.Should().Be(10m);
        harness.Transport.Errors(endpoint).Should().BeEmpty("the event is accepted, not parked");

        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new OrderCancelled(orderId));
        (await WaitForAsync(async () => await repository.FindAsync(orderId, CancellationToken.None) is null)).Should().BeTrue();
    }

    [Fact]
    public async Task D5_a_caught_throw_discards_the_attempt_and_runs_the_compensation_on_the_original_saga()
    {
        var endpoint = $"branching-{Guid.NewGuid():N}";
        var orderId = Guid.NewGuid();
        await using var harness = await StartAsync(endpoint);
        var repository = harness.Services.GetRequiredService<ISagaRepository<OrderSaga>>();
        var observed = harness.Services.GetRequiredService<Observed>();

        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new OrderSubmitted(orderId, 42m));
        (await WaitForAsync(async () => await repository.FindAsync(orderId, CancellationToken.None) is not null)).Should().BeTrue();
        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new OrderRejected(orderId));

        (await WaitForAsync(() => observed.Messages.OfType<OrderFailed>().Any(m => m.OrderId == orderId))).Should().BeTrue("the compensation published its failure");
        await Task.Delay(300);
        observed.Messages.OfType<OrderAccepted>().Should().BeEmpty("the publish from the failed attempt was discarded");
        var stored = await repository.FindAsync(orderId, CancellationToken.None);
        stored!.Amount.Should().Be(42m, "the attempt's change to the amount was rolled back");
        stored.CurrentState.Should().Be("Submitted");
        harness.Transport.Errors(endpoint).Should().BeEmpty("a caught throw is not parked");
    }

    [Fact]
    public async Task D5_a_throw_no_catch_matches_leaves_the_saga_unchanged_and_parks_the_message()
    {
        var endpoint = $"branching-{Guid.NewGuid():N}";
        var orderId = Guid.NewGuid();
        await using var harness = await StartAsync(endpoint);
        var repository = harness.Services.GetRequiredService<ISagaRepository<OrderSaga>>();

        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new OrderSubmitted(orderId, 42m));
        (await WaitForAsync(async () => await repository.FindAsync(orderId, CancellationToken.None) is not null)).Should().BeTrue();
        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new OrderFlagged(orderId));

        (await WaitForAsync(() => harness.Transport.Errors(endpoint).Count == 1)).Should().BeTrue("the throw is not one the catch names");
        var stored = await repository.FindAsync(orderId, CancellationToken.None);
        stored!.Amount.Should().Be(42m);
        stored.CurrentState.Should().Be("Submitted");
    }

    [Fact]
    public void D5_a_block_inside_a_block_is_refused_when_the_machine_is_built()
    {
        Action build = () => _ = new NestedBlockStateMachine();

        build.Should().Throw<InvalidOperationException>().WithMessage("*do not nest*");
    }

    private static Task<MessagingTestHarness> StartAsync(string endpoint) =>
        MessagingTestHarness.StartAsync(
            b => b
                .AddSaga<BranchingOrderStateMachine, OrderSaga>(endpoint)
                .AddConsumer<ObservedConsumer<OrderAccepted>, OrderAccepted>($"accepted-{endpoint}")
                .AddConsumer<ObservedConsumer<OrderFailed>, OrderFailed>($"failed-{endpoint}")
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
