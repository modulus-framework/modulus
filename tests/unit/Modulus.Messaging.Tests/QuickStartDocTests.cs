using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

/// <summary>
/// Compiles and runs the in-memory parts of docs/messaging/quick-start.md, so the documented code keeps working. The
/// broker parts (RabbitMQ and PostgreSQL) are not started here: they need a broker.
/// </summary>
[Trait("Category", "Unit")]
public sealed class QuickStartDocTests
{
    [MessageUrn("urn:orders:quick-start-submitted:v1")]
    public sealed record QuickStartSubmitted(Guid OrderId);

    public sealed class QuickStartBilling(Attempts attempts) : IConsumer<QuickStartSubmitted>
    {
        public Task Consume(ConsumeContext<QuickStartSubmitted> context)
        {
            attempts.Next();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Section_2_and_3_in_memory_publish_and_send_reach_the_consumer()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b =>
        {
            b.AddConsumer<QuickStartBilling, QuickStartSubmitted>("billing");
            b.UseInMemory();
        }, s => s.AddSingleton<Attempts>());

        await harness.Bus.Publish(new QuickStartSubmitted(Guid.NewGuid()));
        await harness.Bus.Send(new Uri("queue:billing"), new QuickStartSubmitted(Guid.NewGuid()));

        var attempts = harness.Services.GetRequiredService<Attempts>();
        (await MessagingTestHarness.WaitUntilAsync(() => attempts.Value == 2)).Should().BeTrue();
    }

    [Fact]
    public async Task Section_5_retry_and_ignore_are_configured_as_documented()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b =>
        {
            b.AddConsumer<QuickStartBilling, QuickStartSubmitted>("billing-doc", o =>
            {
                o.Retry = RetryPolicy.Immediate(3).Ignore<FormatException>();
                o.Redelivery = RedeliveryPolicy.Intervals(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20));
            });
            b.UseInMemory();
        }, s => s.AddSingleton<Attempts>());

        await harness.Bus.Publish(new QuickStartSubmitted(Guid.NewGuid()));

        // The consumer does not fail here, so the attempt succeeds and the policies are not used. The check is that the
        // configuration above is accepted by the bus at start.
        var attempts = harness.Services.GetRequiredService<Attempts>();
        (await MessagingTestHarness.WaitUntilAsync(() => attempts.Value == 1)).Should().BeTrue();
    }
}
