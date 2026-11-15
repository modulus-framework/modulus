using Modulus.Messaging.Sagas;

namespace Modulus.Messaging.Sagas.Tests;

public sealed record OrderRejected(Guid OrderId);

public sealed record OrderFlagged(Guid OrderId);

/// <summary>Branches (If), a catch that rolls back (Catch) and an uncaught throw, on the order saga.</summary>
public sealed class BranchingOrderStateMachine : SagaStateMachine<OrderSaga>
{
    public BranchingOrderStateMachine()
    {
        var submitted = State("Submitted");
        var submittedEvent = Event<OrderSubmitted>().CorrelateBy(message => message.OrderId);
        var paymentEvent = Event<PaymentReceived>().CorrelateBy(message => message.OrderId);
        var cancelledEvent = Event<OrderCancelled>().CorrelateBy(message => message.OrderId);
        var rejectedEvent = Event<OrderRejected>().CorrelateBy(message => message.OrderId);
        var flaggedEvent = Event<OrderFlagged>().CorrelateBy(message => message.OrderId);

        Initially(
            When(submittedEvent)
                .Then(context => context.Saga.Amount = context.Message.Amount)
                .TransitionTo(submitted));

        During(
            submitted,
            When(paymentEvent)
                .If(
                    context => context.Saga.Amount >= 100,
                    large => large.Then(context => context.Publish(new OrderAccepted(context.Message.OrderId))).Complete()),
            When(rejectedEvent)
                .Then(context =>
                {
                    context.Saga.Amount = 999;
                    context.Publish(new OrderAccepted(context.Message.OrderId));
                    throw new InvalidOperationException("the payment processor refused");
                })
                .Catch<InvalidOperationException>(compensation =>
                    compensation.Then(context => context.Publish(new OrderFailed(context.Message.OrderId)))),
            When(flaggedEvent)
                .Then(context => throw new ArgumentException("not a failure the reaction catches"))
                .Catch<InvalidOperationException>(compensation => compensation.Then(context => context.Publish(new OrderFailed(context.Message.OrderId)))),
            When(cancelledEvent).Complete());
    }
}

/// <summary>A block inside a block: refused when the machine is built.</summary>
public sealed class NestedBlockStateMachine : SagaStateMachine<OrderSaga>
{
    public NestedBlockStateMachine()
    {
        var submitted = State("Submitted");
        var submittedEvent = Event<OrderSubmitted>().CorrelateBy(message => message.OrderId);
        var paymentEvent = Event<PaymentReceived>().CorrelateBy(message => message.OrderId);

        Initially(When(submittedEvent).TransitionTo(submitted));
        During(
            submitted,
            When(paymentEvent).If(
                _ => true,
                outer => outer.If(_ => true, inner => inner.Complete())));
    }
}
