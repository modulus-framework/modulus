using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

[Trait("Category", "Unit")]
public sealed class TracingTests
{
    [Fact]
    public async Task FR_24_a_consumer_span_joins_the_producer_trace()
    {
        var finished = new ConcurrentQueue<Activity>();
        using var listener = Listen(finished);
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<CreateInvoiceConsumer, CreateInvoice>("billing.trace")
            .UseInMemory(), services => services.AddSingleton<Sink>());

        using var root = new Activity("checkout").Start();
        await harness.Bus.Send(new Uri("queue:billing.trace"), new CreateInvoice(Guid.NewGuid()));
        var sink = harness.Services.GetRequiredService<Sink>();
        (await MessagingTestHarness.WaitUntilAsync(() => sink.Calls.Count >= 1)).Should().BeTrue();
        await Task.Delay(50);

        var producer = finished.Single(a => a.Kind == ActivityKind.Producer && a.DisplayName == "send billing.trace");
        var consumer = finished.Single(a => a.Kind == ActivityKind.Consumer && a.DisplayName == "process billing.trace");
        producer.TraceId.Should().Be(root.TraceId, "the send runs inside the caller's trace");
        producer.ParentSpanId.Should().Be(root.SpanId);
        consumer.TraceId.Should().Be(root.TraceId, "the consumer continues the producer's trace");
        consumer.ParentSpanId.Should().Be(producer.SpanId, "the consumer span is a child of the send span");
        consumer.GetTagItem("messaging.destination.name").Should().Be("billing.trace");
        // D9: the span names the transport, not a generic system.
        producer.GetTagItem("messaging.system").Should().Be("in-memory");
        consumer.GetTagItem("messaging.system").Should().Be("in-memory");
    }

    [Fact]
    public async Task FR_24_a_failed_consume_marks_its_span_as_error()
    {
        var finished = new ConcurrentQueue<Activity>();
        using var listener = Listen(finished);
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<ThrowingConsumer, CreateInvoice>("billing.failing")
            .UseInMemory(), services => services.AddSingleton<Sink>());

        await harness.Bus.Send(new Uri("queue:billing.failing"), new CreateInvoice(Guid.NewGuid()));
        (await MessagingTestHarness.WaitUntilAsync(() => finished.Any(a => a.Kind == ActivityKind.Consumer && a.Status == ActivityStatusCode.Error))).Should().BeTrue();
    }

    private static ActivityListener Listen(ConcurrentQueue<Activity> finished)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Modulus.Messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = finished.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}

public sealed class ThrowingConsumer : IConsumer<CreateInvoice>
{
    public Task Consume(ConsumeContext<CreateInvoice> context) => throw new InvalidOperationException("boom");
}
