using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

[Trait("Category", "Unit")]
public sealed class BusTests
{
    private static void RegisterSink(IServiceCollection services) => services.AddSingleton<Sink>();

    [Fact]
    public async Task FR_01_publish_reaches_each_subscribed_endpoint_once_including_interface_subscribers()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<BillingConsumer, OrderSubmitted>("billing")
            .AddConsumer<ShippingConsumer, OrderSubmitted>("shipping")
            .AddConsumer<AuditConsumer, IOrderEvent>("audit")
            .UseInMemory(), RegisterSink);

        await harness.Bus.Publish(new OrderSubmitted(Guid.NewGuid()));

        var sink = harness.Services.GetRequiredService<Sink>();
        (await MessagingTestHarness.WaitUntilAsync(() => sink.Calls.Count == 3)).Should().BeTrue();
        await Task.Delay(50);

        sink.CountFor("billing").Should().Be(1);
        sink.CountFor("shipping").Should().Be(1);
        sink.CountFor("audit").Should().Be(1);
    }

    [Fact]
    public async Task FR_02_send_to_queue_address_is_consumed_by_exactly_one_consumer()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<CreateInvoiceConsumer, CreateInvoice>("billing.create-invoice")
            .UseInMemory(), RegisterSink);

        await harness.Bus.Send(new Uri("queue:billing.create-invoice"), new CreateInvoice(Guid.NewGuid()));

        var sink = harness.Services.GetRequiredService<Sink>();
        (await MessagingTestHarness.WaitUntilAsync(() => sink.Calls.Count >= 1)).Should().BeTrue();
        await Task.Delay(50);
        sink.Calls.Should().ContainSingle().Which.Should().Be("billing.create-invoice:create-invoice");
    }

    [Fact]
    public async Task FR_03_each_message_is_handled_in_its_own_scope_with_fresh_scoped_services()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<ScopedConsumer, OrderSubmitted>("scoped")
            .UseInMemory(), services =>
            {
                RegisterSink(services);
                services.AddScoped<ScopedMarker>();
            });

        await harness.Bus.Publish(new OrderSubmitted(Guid.NewGuid()));
        await harness.Bus.Publish(new OrderSubmitted(Guid.NewGuid()));

        var sink = harness.Services.GetRequiredService<Sink>();
        (await MessagingTestHarness.WaitUntilAsync(() => sink.ScopedIds.Count == 2)).Should().BeTrue();
        sink.ScopedIds.Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task Endpoint_defaults_to_the_consumer_name_in_kebab_case_without_the_suffix()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<OrderStatusConsumer, OrderSubmitted>()
            .UseInMemory(), RegisterSink);

        await harness.Bus.Publish(new OrderSubmitted(Guid.NewGuid()));

        var sink = harness.Services.GetRequiredService<Sink>();
        (await MessagingTestHarness.WaitUntilAsync(() => sink.Calls.Count == 1)).Should().BeTrue();
        sink.Calls.Should().ContainSingle().Which.Should().Be("order-status:status");
    }

    [Fact]
    public async Task FR_C3_a_failing_consumer_parks_the_message_in_error_on_the_first_failure()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<FailingConsumer, OrderSubmitted>("billing")
            .UseInMemory());

        await harness.Bus.Publish(new OrderSubmitted(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Errors("billing").Count == 1)).Should().BeTrue();
        var letter = harness.Transport.Errors("billing").Single();
        letter.Exception.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("boom");
        letter.Envelope.Endpoint.Should().Be("billing");
    }

    [Fact]
    public async Task UC_03_a_message_no_consumer_on_the_endpoint_handles_is_parked_in_skipped()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<BillingConsumer, OrderSubmitted>("billing")
            .UseInMemory(), RegisterSink);

        await harness.Bus.Send(new Uri("queue:billing"), new Ping());

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Skipped("billing").Count == 1)).Should().BeTrue();
        harness.Transport.Errors("billing").Should().BeEmpty();
    }

    [Fact]
    public async Task FR_02_send_rejects_addresses_that_do_not_name_a_queue()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b.UseInMemory());

        var act = () => harness.Bus.Send(new Uri("http://example.com/orders"), new Ping());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void Building_without_a_transport_fails_with_a_clear_message()
    {
        var services = new ServiceCollection();

        var act = () => services.AddModulusMessaging(b => b.AddConsumer<BillingConsumer, OrderSubmitted>("billing"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*No transport is configured*");
    }
}
