using System.Collections.Concurrent;
using Modulus.Messaging.Sagas;

namespace Modulus.Messaging.Sagas.Tests;

public sealed class OrderSaga : SagaInstance
{
    public decimal Amount { get; set; }
}

public sealed record OrderSubmitted(Guid OrderId, decimal Amount);

public sealed record PaymentReceived(Guid OrderId);

public sealed record OrderCancelled(Guid OrderId);

public sealed record OrderTimedOut(Guid OrderId);

public sealed record OrderAccepted(Guid OrderId);

public sealed record OrderFailed(Guid OrderId);

public sealed class OrderStateMachine : SagaStateMachine<OrderSaga>
{
    public static readonly TimeSpan PaymentWindow = TimeSpan.FromSeconds(2);

    public OrderStateMachine()
    {
        var submitted = State("Submitted");
        var submittedEvent = Event<OrderSubmitted>().CorrelateBy(message => message.OrderId);
        var paymentEvent = Event<PaymentReceived>().CorrelateBy(message => message.OrderId);
        var cancelledEvent = Event<OrderCancelled>().CorrelateBy(message => message.OrderId);
        var timedOutEvent = Event<OrderTimedOut>().CorrelateBy(message => message.OrderId);

        Initially(
            When(submittedEvent)
                .Then(context =>
                {
                    context.Saga.Amount = context.Message.Amount;
                    context.Schedule(PaymentWindow, new OrderTimedOut(context.Message.OrderId));
                })
                .TransitionTo(submitted));

        During(
            submitted,
            When(paymentEvent)
                .Then(context => context.Publish(new OrderAccepted(context.Message.OrderId)))
                .Complete(),
            When(timedOutEvent)
                .Then(context => context.Publish(new OrderFailed(context.Message.OrderId)))
                .Complete(),
            When(cancelledEvent).Complete());
    }
}

/// <summary>An event with no correlation key: the machine must refuse to start.</summary>
public sealed class UncorrelatedOrderStateMachine : SagaStateMachine<OrderSaga>
{
    public UncorrelatedOrderStateMachine() => Event<OrderCancelled>();
}

/// <summary>Records what the probe consumers receive, so tests can check what the saga published.</summary>
public sealed class Observed
{
    public ConcurrentQueue<object> Messages { get; } = new();
}

public sealed class ObservedConsumer<T>(Observed observed) : IConsumer<T>
    where T : class
{
    public Task Consume(ConsumeContext<T> context)
    {
        observed.Messages.Enqueue(context.Message);
        return Task.CompletedTask;
    }
}
