using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Sagas.Tests;

/// <summary>The saga request step (FR-17) with FR-06 faults: the answer and the failure both reach the saga.</summary>
[Trait("Category", "Unit")]
public sealed class SagaRequestStepTests
{
    private const string SagaEndpoint = "quote-saga";

    [Fact]
    public async Task A_consumer_failure_reaches_the_saga_as_a_fault_and_completes_it()
    {
        var orderId = Guid.NewGuid();
        await using var harness = await StartAsync<FailingPricer>();
        var observed = harness.Services.GetRequiredService<Observed>();

        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new FulfillStarted(orderId));

        (await WaitForAsync(() => observed.Messages.OfType<OrderQuoteFailed>().Any())).Should().BeTrue(
            "the pricer failed, so its fault reached the waiting saga");
        var failed = observed.Messages.OfType<OrderQuoteFailed>().Single();
        failed.ExceptionType.Should().Be(typeof(InvalidOperationException).FullName);
        observed.Messages.OfType<OrderQuoteReceived>().Should().BeEmpty("no answer came");
    }

    [Fact]
    public async Task A_consumer_answer_reaches_the_saga_and_completes_it_as_received()
    {
        var orderId = Guid.NewGuid();
        await using var harness = await StartAsync<ReplyingPricer>();
        var observed = harness.Services.GetRequiredService<Observed>();

        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new FulfillStarted(orderId));

        (await WaitForAsync(() => observed.Messages.OfType<OrderQuoteReceived>().Any(m => m.OrderId == orderId))).Should().BeTrue(
            "the pricer's answer reached the waiting saga");
        observed.Messages.OfType<OrderQuoteFailed>().Should().BeEmpty();
    }

    private static Task<MessagingTestHarness> StartAsync<TPricer>() where TPricer : class, IConsumer<QuoteRequested> =>
        MessagingTestHarness.StartAsync(
            b => b
                .AddSaga<QuoteRequestMachine, QuotedSaga>(SagaEndpoint)
                .AddConsumer<TPricer, QuoteRequested>("quote-pricer", o => o.Retry = RetryPolicy.Immediate(1))
                .AddConsumer<ObservedConsumer<OrderQuoteFailed>, OrderQuoteFailed>("probe-failed")
                .AddConsumer<ObservedConsumer<OrderQuoteReceived>, OrderQuoteReceived>("probe-received")
                .UseInMemory(),
            services => services
                .AddInMemorySagaRepository<QuotedSaga>()
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
