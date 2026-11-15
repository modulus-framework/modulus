using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Sagas;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Sagas.Tests;

public sealed record ReservationRequested(Guid ReservationId, int Seats);

public sealed record ReservationPaid(Guid ReservationId);

public sealed record ReservationExpired(Guid ReservationId);

public sealed record ReservationConfirmed(Guid ReservationId, int Seats);

public sealed record ReservationFailed(Guid ReservationId);

public sealed class ReservationSaga :
    SagaInstance,
    ICorrelatedBy<ReservationRequested>,
    ICorrelatedBy<ReservationPaid>,
    ICorrelatedBy<ReservationExpired>,
    IInitiatedBy<ReservationSaga, ReservationRequested>,
    IOrchestrates<ReservationSaga, ReservationPaid>,
    IOrchestrates<ReservationSaga, ReservationExpired>
{
    public static readonly TimeSpan HoldWindow = TimeSpan.FromSeconds(2);

    public int Seats { get; set; }

    public static Guid CorrelationKeyOf(ReservationRequested message) => message.ReservationId;

    public static Guid CorrelationKeyOf(ReservationPaid message) => message.ReservationId;

    public static Guid CorrelationKeyOf(ReservationExpired message) => message.ReservationId;

    public void Initiate(SagaContext<ReservationSaga, ReservationRequested> context)
    {
        Seats = context.Message.Seats;
        CurrentState = "Held";
        context.Schedule(HoldWindow, new ReservationExpired(context.Message.ReservationId));
    }

    public void Orchestrate(SagaContext<ReservationSaga, ReservationPaid> context)
    {
        CurrentState = "Paid";
        context.Publish(new ReservationConfirmed(context.Message.ReservationId, Seats));
        context.Complete();
    }

    public void Orchestrate(SagaContext<ReservationSaga, ReservationExpired> context)
    {
        context.Publish(new ReservationFailed(context.Message.ReservationId));
        context.Complete();
    }
}

/// <summary>A saga that initiates on a message it cannot correlate: the registration must refuse it.</summary>
public sealed class UncorrelatedReservationSaga : SagaInstance, IInitiatedBy<UncorrelatedReservationSaga, ReservationRequested>
{
    public void Initiate(SagaContext<UncorrelatedReservationSaga, ReservationRequested> context)
    {
    }
}

[Trait("Category", "Unit")]
public sealed class ConsumerSagaTests
{
    private const string SagaEndpoint = "reservations-saga";

    [Fact]
    public async Task FR_18_an_initiating_message_creates_the_instance_and_a_later_one_moves_it_on_and_completes()
    {
        var reservationId = Guid.NewGuid();
        await using var harness = await StartAsync(configure: null);
        var repository = harness.Services.GetRequiredService<ISagaRepository<ReservationSaga>>();
        var observed = harness.Services.GetRequiredService<Observed>();

        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new ReservationRequested(reservationId, 4));
        (await WaitForAsync(async () => (await repository.FindAsync(reservationId, CancellationToken.None))?.CurrentState == "Held")).Should().BeTrue();
        (await repository.FindAsync(reservationId, CancellationToken.None))!.Seats.Should().Be(4);

        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new ReservationPaid(reservationId));

        (await WaitForAsync(() => observed.Messages.OfType<ReservationConfirmed>().Any(m => m.ReservationId == reservationId && m.Seats == 4))).Should().BeTrue();
        (await WaitForAsync(async () => await repository.FindAsync(reservationId, CancellationToken.None) is null)).Should().BeTrue("a completed saga is removed");
    }

    [Fact]
    public async Task FR_18_a_message_that_arrives_before_its_instance_is_redelivered_and_handled()
    {
        var reservationId = Guid.NewGuid();
        await using var harness = await StartAsync(o => o.Redelivery = RedeliveryPolicy.Intervals(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(100)));
        var observed = harness.Services.GetRequiredService<Observed>();

        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new ReservationPaid(reservationId));
        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new ReservationRequested(reservationId, 2));

        (await WaitForAsync(() => observed.Messages.OfType<ReservationConfirmed>().Any(m => m.ReservationId == reservationId))).Should().BeTrue();
    }

    [Fact]
    public async Task FR_18_a_second_initiating_message_for_an_existing_instance_changes_nothing()
    {
        var reservationId = Guid.NewGuid();
        await using var harness = await StartAsync(configure: null);
        var repository = harness.Services.GetRequiredService<ISagaRepository<ReservationSaga>>();

        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new ReservationRequested(reservationId, 4));
        (await WaitForAsync(async () => await repository.FindAsync(reservationId, CancellationToken.None) is not null)).Should().BeTrue();

        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new ReservationRequested(reservationId, 9));
        await Task.Delay(300);

        (await repository.FindAsync(reservationId, CancellationToken.None))!.Seats.Should().Be(4, "the instance exists, so the second request is not an initiation");
    }

    [Fact]
    public async Task FR_18_a_timeout_that_orchestrates_the_saga_publishes_the_failure_and_completes()
    {
        var reservationId = Guid.NewGuid();
        await using var harness = await StartAsync(configure: null);
        var repository = harness.Services.GetRequiredService<ISagaRepository<ReservationSaga>>();
        var observed = harness.Services.GetRequiredService<Observed>();

        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new ReservationRequested(reservationId, 1));

        (await WaitForAsync(() => observed.Messages.OfType<ReservationFailed>().Any(m => m.ReservationId == reservationId))).Should().BeTrue("the schedule fired the expiry");
        (await WaitForAsync(async () => await repository.FindAsync(reservationId, CancellationToken.None) is null)).Should().BeTrue();
    }

    [Fact]
    public void A_saga_that_handles_a_message_it_cannot_correlate_is_refused_at_registration()
    {
        var builder = new MessagingBusBuilder();

        Action act = () => builder.AddConsumerSaga<UncorrelatedReservationSaga>("uncorrelated");

        act.Should().Throw<InvalidOperationException>().WithMessage("*ICorrelatedBy*");
    }

    private static Task<MessagingTestHarness> StartAsync(Action<ConsumerOptions>? configure) =>
        MessagingTestHarness.StartAsync(
            b => b
                .AddConsumerSaga<ReservationSaga>(SagaEndpoint, configure)
                .AddConsumer<ObservedConsumer<ReservationConfirmed>, ReservationConfirmed>("probe-confirmed")
                .AddConsumer<ObservedConsumer<ReservationFailed>, ReservationFailed>("probe-failed")
                .UseInMemory(),
            services => services
                .AddInMemorySagaRepository<ReservationSaga>()
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
