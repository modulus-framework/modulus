using Modulus.Messaging;
using Modulus.Messaging.Sagas;

namespace Modulus.Messaging.Sagas.Tests;

public sealed class QuotedSaga : SagaInstance
{
    public string? Note { get; set; }
}

public sealed record FulfillStarted(Guid OrderId);

public sealed record QuoteRequested(Guid OrderId);

public sealed record OrderQuoted(Guid OrderId, decimal Price);

public sealed record OrderQuoteReceived(Guid OrderId);

public sealed record OrderQuoteFailed(Guid RequestId, string ExceptionType);

/// <summary>
/// A saga request step (FR-17): it asks the pricer and waits in <c>Waiting</c>. The answer completes it as received; a
/// failure on the pricer's side (a <see cref="RequestFault"/>, correlated by the request id) completes it as failed.
/// </summary>
public sealed class QuoteRequestMachine : SagaStateMachine<QuotedSaga>
{
    public QuoteRequestMachine()
    {
        var waiting = State("Waiting");
        var started = Event<FulfillStarted>().CorrelateBy(message => message.OrderId);
        var quoted = Event<OrderQuoted>().CorrelateBy(message => message.OrderId);
        var failed = Event<RequestFault>().CorrelateBy(fault => fault.RequestId);

        Initially(
            When(started)
                .Then(context => context.Request(new Uri("queue:quote-pricer"), new QuoteRequested(context.Message.OrderId)))
                .TransitionTo(waiting));

        During(
            waiting,
            When(quoted)
                .Then(context => context.Publish(new OrderQuoteReceived(context.Message.OrderId)))
                .Complete(),
            When(failed)
                .Then(context => context.Publish(new OrderQuoteFailed(context.Message.RequestId, context.Message.ExceptionType)))
                .Complete());
    }
}

/// <summary>Fails every quote: the bus parks the request and sends its fault to the saga.</summary>
public sealed class FailingPricer : IConsumer<QuoteRequested>
{
    public Task Consume(ConsumeContext<QuoteRequested> context) =>
        throw new InvalidOperationException("no price for this order");
}

/// <summary>Answers every quote with a price, the way a remote service answers a request.</summary>
public sealed class ReplyingPricer(IBus bus) : IConsumer<QuoteRequested>
{
    public Task Consume(ConsumeContext<QuoteRequested> context) =>
        bus.Send(new Uri("queue:quote-saga"), new OrderQuoted(context.Message.OrderId, 9m), context.CancellationToken);
}
