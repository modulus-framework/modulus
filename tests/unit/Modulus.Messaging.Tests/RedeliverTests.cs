using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

/// <summary>FR-08: a consumer asks for the message to come back later with <c>context.Redeliver(delay)</c>.</summary>
[Trait("Category", "Unit")]
public sealed class RedeliverTests
{
    public sealed class RedeliverTimes
    {
        public ConcurrentQueue<DateTimeOffset> Attempts { get; } = new();
    }

    /// <summary>Asks for a redelivery on its first attempt and succeeds on the second.</summary>
    public sealed class RedeliverOnceConsumer(Attempts attempts, RedeliverTimes times) : IConsumer<OrderSubmitted>
    {
        public Task Consume(ConsumeContext<OrderSubmitted> context)
        {
            times.Attempts.Enqueue(DateTimeOffset.UtcNow);
            if (attempts.Next() == 1)
            {
                context.Redeliver(TimeSpan.FromMilliseconds(150));
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task FR_08_a_redeliver_request_comes_back_after_its_delay_and_is_not_an_error()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<RedeliverOnceConsumer, OrderSubmitted>("billing-redeliver")
            .UseInMemory(), services =>
            {
                services.AddSingleton<Attempts>();
                services.AddSingleton<RedeliverTimes>();
            });

        await harness.Bus.Publish(new OrderSubmitted(Guid.NewGuid()));

        var times = harness.Services.GetRequiredService<RedeliverTimes>();
        (await MessagingTestHarness.WaitUntilAsync(() => times.Attempts.Count == 2)).Should().BeTrue();
        harness.Services.GetRequiredService<Attempts>().Value.Should().Be(2);
        harness.Transport.Errors("billing-redeliver").Should().BeEmpty("a redelivery request is not a failure");

        var stamps = times.Attempts.ToArray();
        (stamps[1] - stamps[0]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(120),
            "the message waits for the requested delay before it is delivered again");
    }

    [Fact]
    public void FR_08_a_negative_redelivery_delay_is_refused()
    {
        var act = () => new RedeliverMessageException(TimeSpan.FromSeconds(-1));

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("delay");
    }
}
