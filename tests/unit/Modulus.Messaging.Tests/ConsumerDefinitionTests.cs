using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

/// <summary>FR-03: a consumer's endpoint and settings can live in a <see cref="ConsumerDefinition{TConsumer}"/>.</summary>
[Trait("Category", "Unit")]
public sealed class ConsumerDefinitionTests
{
    public sealed class BillingDefinition : ConsumerDefinition<AlwaysFailingConsumer>
    {
        public override string? EndpointName => "definition-billing";

        public override void Configure(ConsumerOptions options) =>
            options.Redelivery = RedeliveryPolicy.Intervals(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20));
    }

    public sealed class DefaultEndpointDefinition : ConsumerDefinition<AlwaysFailingConsumer>
    {
        public override void Configure(ConsumerOptions options) => options.Redelivery = null;
    }

    [Fact]
    public async Task FR_03_the_definition_sets_the_endpoint_and_the_settings_of_its_consumer()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<AlwaysFailingConsumer, OrderSubmitted>(new BillingDefinition())
            .UseInMemory(), services => services.AddSingleton<Attempts>());

        await harness.Bus.Publish(new OrderSubmitted(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Errors("definition-billing").Count == 1)).Should().BeTrue();
        // Two redeliveries from the definition's policy: three attempts in all, on the definition's endpoint.
        harness.Services.GetRequiredService<Attempts>().Value.Should().Be(3);
    }

    [Fact]
    public async Task FR_03_a_definition_without_an_endpoint_name_uses_the_consumer_default_endpoint()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<AlwaysFailingConsumer, OrderSubmitted>(new DefaultEndpointDefinition())
            .UseInMemory(), services => services.AddSingleton<Attempts>());

        await harness.Bus.Publish(new OrderSubmitted(Guid.NewGuid()));

        var defaultEndpoint = await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Errors("always-failing").Count == 1);
        defaultEndpoint.Should().BeTrue("the endpoint is the consumer's name in kebab-case, as AddConsumer does without a name");
        harness.Services.GetRequiredService<Attempts>().Value.Should().Be(1, "this definition sets no redelivery");
    }
}
