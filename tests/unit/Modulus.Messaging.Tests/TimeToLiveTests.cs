using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

[Trait("Category", "Unit")]
public sealed class TimeToLiveTests
{
    [Fact]
    public async Task FR_13_an_expired_message_goes_to_skipped_unprocessed()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<CreateInvoiceConsumer, CreateInvoice>("billing.create-invoice", o => o.TimeToLive = TimeSpan.FromMilliseconds(50))
            .UseInMemory(), services => services.AddSingleton<Sink>());
        var sink = harness.Services.GetRequiredService<Sink>();
        var scheduler = harness.Services.GetRequiredService<IMessageScheduler>();

        // Expiry is counted from the send (50 ms). The message is due at 300 ms, so it arrives after it expired.
        await scheduler.ScheduleSend(new Uri("queue:billing.create-invoice"), DateTimeOffset.UtcNow.AddMilliseconds(300), new CreateInvoice(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Skipped("billing.create-invoice").Count == 1, TimeSpan.FromSeconds(5))).Should().BeTrue();
        await Task.Delay(50);
        sink.Calls.Should().BeEmpty("an expired message is never processed");
        harness.Transport.Skipped("billing.create-invoice")[0].Envelope.Headers[MessagingHeaders.SkipReason].Should().Be("expired");
    }

    [Fact]
    public async Task FR_13_a_message_within_its_time_to_live_is_consumed()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<CreateInvoiceConsumer, CreateInvoice>("billing.create-invoice", o => o.TimeToLive = TimeSpan.FromSeconds(30))
            .UseInMemory(), services => services.AddSingleton<Sink>());
        var sink = harness.Services.GetRequiredService<Sink>();

        await harness.Bus.Send(new Uri("queue:billing.create-invoice"), new CreateInvoice(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() => sink.Calls.Count == 1)).Should().BeTrue();
        harness.Transport.Skipped("billing.create-invoice").Should().BeEmpty();
    }

    [Fact]
    public async Task FR_13_a_message_type_attribute_sets_its_time_to_live_and_overrides_the_consumer()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<ShortLivedConsumer, ShortLived>("short-lived", o => o.TimeToLive = TimeSpan.FromMinutes(10))
            .UseInMemory(), services => services.AddSingleton<Sink>());
        var sink = harness.Services.GetRequiredService<Sink>();
        var scheduler = harness.Services.GetRequiredService<IMessageScheduler>();

        // The attribute (50 ms) is shorter than the consumer's ten minutes, so the scheduled message expires first.
        await scheduler.ScheduleSend(new Uri("queue:short-lived"), DateTimeOffset.UtcNow.AddMilliseconds(300), new ShortLived());

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Skipped("short-lived").Count == 1, TimeSpan.FromSeconds(5))).Should().BeTrue();
        sink.Calls.Should().BeEmpty();
    }
}

[MessageTimeToLive(50)]
public sealed record ShortLived;

public sealed class ShortLivedConsumer(Sink sink) : IConsumer<ShortLived>
{
    public Task Consume(ConsumeContext<ShortLived> context)
    {
        sink.Calls.Enqueue("short-lived:short-lived");
        return Task.CompletedTask;
    }
}
