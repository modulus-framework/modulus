using Modulus.Messaging.Sagas;

namespace Modulus.Messaging.Sagas.Tests;

public sealed class PricedSaga : SagaInstance
{
    public decimal Price { get; set; }
}

public sealed record PriceStarted(Guid OrderId);

public sealed record PriceRequested(Guid OrderId);

public sealed record PriceQuoted(Guid OrderId, decimal Price);

public sealed record OrderPriced(Guid OrderId, decimal Price);

/// <summary>
/// A saga request step: the saga sends the request, moves to <c>Quoting</c>, and the reply is a correlated event that
/// completes it. The reply is matched by the order id the saga stored its instance under.
/// </summary>
public sealed class PricingStateMachine : SagaStateMachine<PricedSaga>
{
    public PricingStateMachine()
    {
        var quoting = State("Quoting");
        var started = Event<PriceStarted>().CorrelateBy(message => message.OrderId);
        var quoted = Event<PriceQuoted>().CorrelateBy(message => message.OrderId);

        Initially(
            When(started)
                .Then(context => context.Send(new Uri("queue:pricing"), new PriceRequested(context.Message.OrderId)))
                .TransitionTo(quoting));

        During(
            quoting,
            When(quoted)
                .Then(context =>
                {
                    context.Saga.Price = context.Message.Price;
                    context.Publish(new OrderPriced(context.Message.OrderId, context.Message.Price));
                })
                .Complete());
    }
}

/// <summary>Replies to the saga's endpoint, the way a remote service answers a request.</summary>
public sealed class PriceResponder(IBus bus) : IConsumer<PriceRequested>
{
    public Task Consume(ConsumeContext<PriceRequested> context) =>
        bus.Send(new Uri("queue:request-reply-saga"), new PriceQuoted(context.Message.OrderId, 7m), context.CancellationToken);
}
