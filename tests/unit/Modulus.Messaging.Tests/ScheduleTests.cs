using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

[Trait("Category", "Unit")]
public sealed class ScheduleTests
{
    private static void RegisterSink(IServiceCollection services) => services.AddSingleton<Sink>();

    [Fact]
    public async Task FR_12_a_scheduled_send_arrives_at_its_time_not_before()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<CreateInvoiceConsumer, CreateInvoice>("billing.create-invoice")
            .UseInMemory(), RegisterSink);
        var sink = harness.Services.GetRequiredService<Sink>();
        var scheduler = harness.Services.GetRequiredService<IMessageScheduler>();

        var clock = Stopwatch.StartNew();
        await scheduler.ScheduleSend(new Uri("queue:billing.create-invoice"), DateTimeOffset.UtcNow.AddMilliseconds(500), new CreateInvoice(Guid.NewGuid()));

        await Task.Delay(150);
        sink.Calls.Should().BeEmpty("the message is not due yet");

        (await MessagingTestHarness.WaitUntilAsync(() => sink.Calls.Count == 1, TimeSpan.FromSeconds(5))).Should().BeTrue();
        clock.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(450), "it arrives at its time, not early");
    }

    [Fact]
    public async Task FR_12_a_scheduled_send_can_be_given_as_a_delay()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<CreateInvoiceConsumer, CreateInvoice>("billing.create-invoice")
            .UseInMemory(), RegisterSink);
        var sink = harness.Services.GetRequiredService<Sink>();
        var scheduler = harness.Services.GetRequiredService<IMessageScheduler>();

        var clock = Stopwatch.StartNew();
        await scheduler.ScheduleSend(new Uri("queue:billing.create-invoice"), TimeSpan.FromMilliseconds(400), new CreateInvoice(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() => sink.Calls.Count == 1, TimeSpan.FromSeconds(5))).Should().BeTrue();
        clock.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(350), "the delay is measured from the call");
    }

    [Fact]
    public async Task FR_12_a_negative_delay_is_refused()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<CreateInvoiceConsumer, CreateInvoice>("billing.create-invoice")
            .UseInMemory(), RegisterSink);
        var scheduler = harness.Services.GetRequiredService<IMessageScheduler>();

        Action act = () => _ = scheduler.SchedulePublish(TimeSpan.FromSeconds(-1), new CreateInvoice(Guid.NewGuid()));

        act.Should().Throw<ArgumentOutOfRangeException>();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task FR_12_a_cancelled_scheduled_message_never_arrives()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<CreateInvoiceConsumer, CreateInvoice>("billing.create-invoice")
            .UseInMemory(), RegisterSink);
        var sink = harness.Services.GetRequiredService<Sink>();
        var scheduler = harness.Services.GetRequiredService<IMessageScheduler>();

        var token = await scheduler.ScheduleSend(new Uri("queue:billing.create-invoice"), DateTimeOffset.UtcNow.AddMilliseconds(300), new CreateInvoice(Guid.NewGuid()));

        (await scheduler.CancelScheduled(token)).Should().BeTrue();
        await Task.Delay(600);

        sink.Calls.Should().BeEmpty("a cancelled message never arrives");
        (await scheduler.CancelScheduled(token)).Should().BeFalse("the token has nothing left to cancel");
    }

    [Fact]
    public async Task FR_12_a_scheduled_publish_reaches_every_consumer_of_the_type_when_it_is_due()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<BillingConsumer, OrderSubmitted>("billing")
            .AddConsumer<ShippingConsumer, OrderSubmitted>("shipping")
            .UseInMemory(), RegisterSink);
        var sink = harness.Services.GetRequiredService<Sink>();
        var scheduler = harness.Services.GetRequiredService<IMessageScheduler>();

        await scheduler.SchedulePublish(DateTimeOffset.UtcNow.AddMilliseconds(200), new OrderSubmitted(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() => sink.Calls.Count == 2, TimeSpan.FromSeconds(5))).Should().BeTrue();
        sink.CountFor("billing").Should().Be(1);
        sink.CountFor("shipping").Should().Be(1);
    }

    [Fact]
    public async Task FR_12_cancelling_a_message_that_was_already_delivered_reports_nothing_to_cancel()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<CreateInvoiceConsumer, CreateInvoice>("billing.create-invoice")
            .UseInMemory(), RegisterSink);
        var sink = harness.Services.GetRequiredService<Sink>();
        var scheduler = harness.Services.GetRequiredService<IMessageScheduler>();

        var token = await scheduler.ScheduleSend(new Uri("queue:billing.create-invoice"), DateTimeOffset.UtcNow, new CreateInvoice(Guid.NewGuid()));
        (await MessagingTestHarness.WaitUntilAsync(() => sink.Calls.Count == 1)).Should().BeTrue();

        (await scheduler.CancelScheduled(token)).Should().BeFalse();
    }

    [Fact]
    public async Task FR_12_scheduling_is_refused_on_a_transport_without_scheduling()
    {
        // The harness supplies its own in-memory transport, so this bus is built directly.
        var services = new ServiceCollection();
        services.AddModulusMessaging(b => b
            .AddConsumer<CreateInvoiceConsumer, CreateInvoice>("billing.create-invoice")
            .UseTransport(_ => new NoSchedulingTransport()));
        await using var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IMessageScheduler>();

        var schedule = async () => await scheduler.ScheduleSend(new Uri("queue:billing.create-invoice"), DateTimeOffset.UtcNow, new CreateInvoice(Guid.NewGuid()));

        await schedule.Should().ThrowAsync<NotSupportedException>().WithMessage("*does not support scheduled messages*");
    }

    /// <summary>A transport that only sends; scheduling is not part of its contract.</summary>
    private sealed class NoSchedulingTransport : IMessageTransport
    {
        public Task Start(IReadOnlyCollection<string> endpoints, Func<MessageEnvelope, CancellationToken, Task> receive, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task Stop(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task Send(MessageEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task Redeliver(MessageEnvelope envelope, TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MoveToError(MessageEnvelope envelope, Exception exception, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MoveToSkipped(MessageEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
