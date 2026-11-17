using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging;
using Modulus.Messaging.Sagas;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Sagas.Tests;

public sealed class TimedQuoteSaga : SagaInstance;

public sealed record TimedStart(Guid OrderId);

public sealed record TimedQuoteRequested(Guid OrderId);

public sealed record TimedQuoteAnswered(Guid OrderId, decimal Price);

public sealed record QuoteTimedOutNotice(Guid OrderId, string RequestType);

public sealed record QuoteAnsweredNotice(Guid OrderId);

/// <summary>
/// A saga request with a timeout (FR-17). It waits in <c>waiting</c>; the timeout completes it, the answer moves it to
/// <c>answered</c>. <c>answered</c> handles no timeout, so a late timeout must be dropped, not parked.
/// </summary>
public abstract class TimedQuoteMachine : SagaStateMachine<TimedQuoteSaga>
{
    protected TimedQuoteMachine(TimeSpan timeout)
    {
        var waiting = State("waiting");
        var answered = State("answered");
        var started = Event<TimedStart>().CorrelateBy(message => message.OrderId);
        var timedOut = Event<SagaRequestTimedOut>().CorrelateBy(message => message.CorrelationId);
        var answer = Event<RequestReply<TimedQuoteAnswered>>().CorrelateBy(reply => reply.RequestId);

        Initially(
            When(started)
                .Then(context => context.Request(new Uri("queue:timed-pricer"), new TimedQuoteRequested(context.Message.OrderId), timeout))
                .TransitionTo(waiting));

        During(
            waiting,
            When(timedOut)
                .Then(context => context.Publish(new QuoteTimedOutNotice(context.Message.CorrelationId, context.Message.RequestType)))
                .Complete(),
            When(answer)
                .Then(context => context.Publish(new QuoteAnsweredNotice(context.Message.RequestId)))
                .TransitionTo(answered));
    }
}

/// <summary>A machine with a 200 ms request timeout. The builder needs the parameterless constructor.</summary>
public sealed class ShortTimeoutMachine : TimedQuoteMachine
{
    public ShortTimeoutMachine() : base(TimeSpan.FromMilliseconds(200))
    {
    }
}

/// <summary>A machine with a 600 ms request timeout.</summary>
public sealed class LongTimeoutMachine : TimedQuoteMachine
{
    public LongTimeoutMachine() : base(TimeSpan.FromMilliseconds(600))
    {
    }
}

/// <summary>The same request timeout in the consumer-saga style: the saga implements the handlers itself.</summary>
public sealed class ConsumerTimedSaga : SagaInstance,
    ICorrelatedBy<TimedStart>,
    IInitiatedBy<ConsumerTimedSaga, TimedStart>,
    ICorrelatedBy<SagaRequestTimedOut>,
    IOrchestrates<ConsumerTimedSaga, SagaRequestTimedOut>
{
    public static Guid CorrelationKeyOf(TimedStart message) => message.OrderId;

    public static Guid CorrelationKeyOf(SagaRequestTimedOut message) => message.CorrelationId;

    public void Initiate(SagaContext<ConsumerTimedSaga, TimedStart> context)
    {
        CurrentState = "waiting";
        context.Request(new Uri("queue:timed-pricer"), new TimedQuoteRequested(context.Message.OrderId), TimeSpan.FromMilliseconds(200));
    }

    public void Orchestrate(SagaContext<ConsumerTimedSaga, SagaRequestTimedOut> context)
    {
        context.Publish(new QuoteTimedOutNotice(context.Message.CorrelationId, context.Message.RequestType));
        context.Complete();
    }
}

/// <summary>Never answers: the request can only time out.</summary>
public sealed class SilentTimedPricer : IConsumer<TimedQuoteRequested>
{
    public Task Consume(ConsumeContext<TimedQuoteRequested> context) => Task.CompletedTask;
}

/// <summary>Answers at once, the way a remote service answers a request.</summary>
public sealed class AnsweringTimedPricer : IConsumer<TimedQuoteRequested>
{
    public Task Consume(ConsumeContext<TimedQuoteRequested> context) =>
        context.RespondAsync(new TimedQuoteAnswered(context.Message.OrderId, 9m), context.CancellationToken);
}

[Trait("Category", "Unit")]
public sealed class SagaRequestTimeoutTests
{
    private const string SagaEndpoint = "timed-saga";

    [Fact]
    public async Task A_request_without_an_answer_times_out_and_the_saga_reacts()
    {
        var orderId = Guid.NewGuid();
        await using var harness = await StartAsync<ShortTimeoutMachine, SilentTimedPricer>();
        var observed = harness.Services.GetRequiredService<Observed>();

        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new TimedStart(orderId));

        (await WaitForAsync(() => observed.Messages.OfType<QuoteTimedOutNotice>().Any())).Should().BeTrue("the timeout reached the saga");
        var notice = observed.Messages.OfType<QuoteTimedOutNotice>().Single();
        notice.OrderId.Should().Be(orderId);
        notice.RequestType.Should().Be(typeof(TimedQuoteRequested).FullName);
        (await harness.Services.GetRequiredService<ISagaRepository<TimedQuoteSaga>>().FindAsync(orderId, CancellationToken.None))
            .Should().BeNull("the timed-out saga completed");
    }

    [Fact]
    public async Task An_answer_closes_the_request_and_its_later_timeout_is_dropped_not_parked()
    {
        var orderId = Guid.NewGuid();
        await using var harness = await StartAsync<LongTimeoutMachine, AnsweringTimedPricer>();
        var observed = harness.Services.GetRequiredService<Observed>();

        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new TimedStart(orderId));

        (await WaitForAsync(() => observed.Messages.OfType<QuoteAnsweredNotice>().Any())).Should().BeTrue("the answer reached the saga");
        // The saga now sits in "answered", which handles no timeout. Wait past the timeout for it to arrive.
        await Task.Delay(1200);

        observed.Messages.OfType<QuoteTimedOutNotice>().Should().BeEmpty("an answered request does not time out");
        harness.Transport.Errors(SagaEndpoint).Should().BeEmpty("the late timeout is dropped, not parked");
        var saga = await harness.Services.GetRequiredService<ISagaRepository<TimedQuoteSaga>>().FindAsync(orderId, CancellationToken.None);
        saga.Should().NotBeNull();
        saga!.CurrentState.Should().Be("answered");
        saga.OpenRequestTimeoutId.Should().BeNull();
    }

    [Fact]
    public async Task In_the_consumer_saga_style_a_request_without_an_answer_times_out_too()
    {
        var orderId = Guid.NewGuid();
        await using var harness = await MessagingTestHarness.StartAsync(
            b => b
                .AddConsumerSaga<ConsumerTimedSaga>("consumer-timed-saga")
                .AddConsumer<SilentTimedPricer, TimedQuoteRequested>("timed-pricer")
                .AddConsumer<ObservedConsumer<QuoteTimedOutNotice>, QuoteTimedOutNotice>("probe-timed-out")
                .UseInMemory(),
            services => services
                .AddInMemorySagaRepository<ConsumerTimedSaga>()
                .AddSingleton<Observed>());
        var observed = harness.Services.GetRequiredService<Observed>();

        await harness.Bus.Send(new Uri("queue:consumer-timed-saga"), new TimedStart(orderId));

        (await WaitForAsync(() => observed.Messages.OfType<QuoteTimedOutNotice>().Any())).Should().BeTrue();
        observed.Messages.OfType<QuoteTimedOutNotice>().Single().OrderId.Should().Be(orderId);
    }

    private static Task<MessagingTestHarness> StartAsync<TMachine, TPricer>()
        where TMachine : SagaStateMachine<TimedQuoteSaga>, new()
        where TPricer : class, IConsumer<TimedQuoteRequested> =>
        MessagingTestHarness.StartAsync(
            b => b
                .AddSaga<TMachine, TimedQuoteSaga>(SagaEndpoint)
                .AddConsumer<TPricer, TimedQuoteRequested>("timed-pricer")
                .AddConsumer<ObservedConsumer<QuoteTimedOutNotice>, QuoteTimedOutNotice>("probe-timed-out")
                .AddConsumer<ObservedConsumer<QuoteAnsweredNotice>, QuoteAnsweredNotice>("probe-answered")
                .UseInMemory(),
            services => services
                .AddInMemorySagaRepository<TimedQuoteSaga>()
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
}
