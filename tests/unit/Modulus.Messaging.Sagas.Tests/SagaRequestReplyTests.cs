using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Sagas.Tests;

[Trait("Category", "Unit")]
public sealed class SagaRequestReplyTests
{
    private const string SagaEndpoint = "request-reply-saga";

    [Fact]
    public async Task A_saga_request_step_is_answered_by_a_correlated_reply_that_completes_the_saga()
    {
        var orderId = Guid.NewGuid();
        await using var harness = await MessagingTestHarness.StartAsync(
            b => b
                .AddSaga<PricingStateMachine, PricedSaga>(SagaEndpoint)
                .AddConsumer<PriceResponder, PriceRequested>("pricing")
                .AddConsumer<ObservedConsumer<OrderPriced>, OrderPriced>("probe-priced")
                .UseInMemory(),
            services => services
                .AddInMemorySagaRepository<PricedSaga>()
                .AddSingleton<Observed>());
        var repository = harness.Services.GetRequiredService<ISagaRepository<PricedSaga>>();
        var observed = harness.Services.GetRequiredService<Observed>();

        await harness.Bus.Send(new Uri($"queue:{SagaEndpoint}"), new PriceStarted(orderId));

        (await WaitForAsync(() => observed.Messages.OfType<OrderPriced>().Any(m => m.OrderId == orderId && m.Price == 7m))).Should().BeTrue(
            "the reply from the responder reached the saga and was published");
        (await WaitForAsync(async () => await repository.FindAsync(orderId, CancellationToken.None) is null)).Should().BeTrue(
            "the saga completed on the reply");
    }

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
