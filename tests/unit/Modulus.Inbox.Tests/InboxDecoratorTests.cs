using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Events;
using Modulus.Events.Abstractions;
using Modulus.Events.Extensions;
using Modulus.Inbox.Abstractions;
using Modulus.Inbox.Extensions;
using Xunit;

namespace Modulus.Inbox.Tests;

/// <summary>
/// Regression coverage for the CRITICAL defect where inbox decoration
/// depended on the registration order of <c>AddModulusEvents(...)</c> vs
/// <c>AddModulus(...)</c>/<c>AddInbox&lt;TContext&gt;(...)</c> in
/// <c>Program.cs</c>, and broke in BOTH orderings the framework ships:
/// <list type="bullet">
///   <item>
///   CLI-generated apps call <c>AddModulus</c> (which runs every module's
///   <c>AddInbox</c>) BEFORE <c>AddModulusEvents</c> registers any handlers —
///   the old descriptor-mutation decorator ran against zero handlers, so the
///   inbox silently provided no dedup at all.
///   </item>
///   <item>
///   TradeFlow calls <c>AddModulusEvents</c> BEFORE <c>AddModulus</c> (N
///   modules, each calling <c>AddInbox</c>) — the old decorator re-wrapped
///   the already-wrapped descriptor on every subsequent <c>AddInbox</c> call,
///   nesting up to N decorators per handler. The outer claim always deferred
///   on the inner claim for the same EventId, so every event dead-lettered
///   without the real handler ever running — logged and counted as a
///   dedup hit, indistinguishable from healthy deduplication.
///   </item>
/// </list>
/// The fix moves wrapping from DI-registration time to dispatch time (see
/// <see cref="IIntegrationEventHandlerDecorator"/>), so it no longer depends
/// on registration order or how many times <c>AddInbox</c> is called.
/// </summary>
[Trait("Category", "Unit")]
public sealed class InboxDecoratorTests
{
    [Fact]
    public async Task AddInbox_DecoratesHandler_AndDispatchesWithoutThrowing()
    {
        DecoratorHandler.CallCount = 0;

        var services = new ServiceCollection();
        services.AddLogging();
        var dbName = "inbox-decorator-" + Guid.NewGuid();
        services.AddDbContext<IdempotentHandlerTests.TestDbContext>(
            o => o.UseInMemoryDatabase(dbName));
        services.AddModulusEvents(typeof(InboxDecoratorTests).Assembly);
        services.AddInbox<IdempotentHandlerTests.TestDbContext>();

        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IModuleBus>();

        var @event = new DecoratorEvent();

        // Dispatch through the in-process bus — the same path a normal
        // in-process publish takes. Must complete without throwing.
        await bus.PublishAsync(@event);

        DecoratorHandler.CallCount.Should().Be(1);

        var db = scope.ServiceProvider.GetRequiredService<IdempotentHandlerTests.TestDbContext>();
        var inbox = db.Set<InboxMessage>().Single(m => m.Id == @event.EventId);
        inbox.Status.Should().Be(InboxStatus.Processed);
        inbox.HandlerName.Should().NotBeNullOrEmpty(
            "the composite key discriminates by handler, not just EventId");
    }

    [Theory]
    [InlineData(true)]  // AddModulusEvents BEFORE AddInbox — the TradeFlow ordering
    [InlineData(false)] // AddModulusEvents AFTER AddInbox — the CLI-template ordering
    public async Task AddInbox_DedupWorks_RegardlessOfRegistrationOrderRelativeToAddModulusEvents(
        bool eventsFirst)
    {
        DecoratorHandler.CallCount = 0;

        var services = new ServiceCollection();
        services.AddLogging();
        var dbName = "inbox-order-" + Guid.NewGuid();
        services.AddDbContext<IdempotentHandlerTests.TestDbContext>(
            o => o.UseInMemoryDatabase(dbName));

        if (eventsFirst)
        {
            services.AddModulusEvents(typeof(InboxDecoratorTests).Assembly);
            services.AddInbox<IdempotentHandlerTests.TestDbContext>();
        }
        else
        {
            services.AddInbox<IdempotentHandlerTests.TestDbContext>();
            services.AddModulusEvents(typeof(InboxDecoratorTests).Assembly);
        }

        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IModuleBus>();

        var @event = new DecoratorEvent();

        // Publish the SAME event twice. Regardless of registration order, the
        // second delivery must be deduplicated, not re-executed and not
        // deferred forever.
        await bus.PublishAsync(@event);
        await bus.PublishAsync(@event);

        DecoratorHandler.CallCount.Should().Be(1,
            "the inbox must dedup identically no matter which of AddModulusEvents/" +
            "AddInbox ran first");
    }

    [Fact]
    public async Task AddInbox_CalledMultipleTimes_DoesNotNestDecorators()
    {
        // Simulates N modules each calling AddInbox<TheirOwnContext>() against
        // ONE shared handler registration (AddModulusEvents runs once, in the
        // host, per the framework's documented pattern) — TradeFlow's shape.
        DecoratorHandler.CallCount = 0;

        var services = new ServiceCollection();
        services.AddLogging();
        var dbName = "inbox-multi-" + Guid.NewGuid();
        services.AddDbContext<IdempotentHandlerTests.TestDbContext>(
            o => o.UseInMemoryDatabase(dbName));
        services.AddModulusEvents(typeof(InboxDecoratorTests).Assembly);

        // Five modules' worth of AddInbox calls (only the first actually wires
        // the store per named-context binding — the rest exercise that
        // repeated calls to the decorator registration stay a no-op).
        for (var i = 0; i < 5; i++)
            services.AddInbox<IdempotentHandlerTests.TestDbContext>();

        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IModuleBus>();

        var @event = new DecoratorEvent();

        // A single publish must invoke the inner handler exactly once — not
        // deferred/dead-lettered by N nested claims of the same EventId.
        await bus.PublishAsync(@event);

        DecoratorHandler.CallCount.Should().Be(1);

        var db = scope.ServiceProvider.GetRequiredService<IdempotentHandlerTests.TestDbContext>();
        db.Set<InboxMessage>().Count(m => m.Id == @event.EventId).Should().Be(1,
            "one handler claiming one event should produce exactly one inbox row");
    }

    [Fact]
    public async Task AddInbox_FanOutToMultipleHandlers_BothHandlersRunExactlyOnce()
    {
        // B2: the dedup key must discriminate by handler, not just EventId —
        // otherwise the first handler to claim marks the event Processed and
        // every OTHER handler subscribed to the same event is skipped forever.
        FirstFanOutHandler.CallCount = 0;
        SecondFanOutHandler.CallCount = 0;

        var services = new ServiceCollection();
        services.AddLogging();
        var dbName = "inbox-fanout-" + Guid.NewGuid();
        services.AddDbContext<IdempotentHandlerTests.TestDbContext>(
            o => o.UseInMemoryDatabase(dbName));
        services.AddModulusEvents(typeof(InboxDecoratorTests).Assembly);
        services.AddInbox<IdempotentHandlerTests.TestDbContext>();

        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IModuleBus>();

        var @event = new FanOutEvent();
        await bus.PublishAsync(@event);

        FirstFanOutHandler.CallCount.Should().Be(1);
        SecondFanOutHandler.CallCount.Should().Be(1);

        var db = scope.ServiceProvider.GetRequiredService<IdempotentHandlerTests.TestDbContext>();
        db.Set<InboxMessage>().Count(m => m.Id == @event.EventId).Should().Be(2,
            "each handler owns an independent claim row for the same EventId");
    }

    [Fact]
    public async Task IntegrationEventDispatcher_DecoratesHandler_AndDispatchesWithoutThrowing()
    {
        // The broker-consumption path (RabbitMQ/Kafka consumers dispatch
        // through IntegrationEventDispatcher, not InProcessModuleBus) must be
        // decorated identically.
        DecoratorHandler.CallCount = 0;

        var services = new ServiceCollection();
        services.AddLogging();
        var dbName = "inbox-dispatcher-" + Guid.NewGuid();
        services.AddDbContext<IdempotentHandlerTests.TestDbContext>(
            o => o.UseInMemoryDatabase(dbName));
        services.AddModulusEvents(typeof(InboxDecoratorTests).Assembly);
        services.AddInbox<IdempotentHandlerTests.TestDbContext>();

        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IntegrationEventDispatcher>();
        var serializer = scope.ServiceProvider.GetRequiredService<IMessageSerializer>();

        var @event = new DecoratorEvent();
        var envelope = new IntegrationEventEnvelope
        {
            EventId = @event.EventId,
            TypeName = IntegrationEventNaming.GetName(typeof(DecoratorEvent)),
            RoutingKey = IntegrationEventNaming.GetName(typeof(DecoratorEvent)),
            OccurredAt = @event.OccurredAt,
            Payload = serializer.Serialize(@event, typeof(DecoratorEvent)),
        };

        var dispatchedFirst = await dispatcher.DispatchAsync(envelope);
        var dispatchedSecond = await dispatcher.DispatchAsync(envelope); // redelivery

        dispatchedFirst.Should().BeTrue();
        dispatchedSecond.Should().BeTrue(); // known routing key — a real dispatch attempt, just deduped
        DecoratorHandler.CallCount.Should().Be(1,
            "the redelivery must be deduped, not re-executed");
    }

    // ── Test doubles ─────────────────────────────────────────────
    public sealed class DecoratorEvent : IIntegrationEvent
    {
        // init, not get-only: a JSON round-trip (the dispatcher test below)
        // must restore the same EventId, not mint a fresh one on every
        // deserialize — see IntegrationEventBase's doc comment for why.
        public Guid EventId { get; init; } = Guid.NewGuid();
        public string EventType { get; init; } = "decorator.event.v1";
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }

    public sealed class DecoratorHandler : IIntegrationEventHandler<DecoratorEvent>
    {
        public static int CallCount;
        public Task HandleAsync(DecoratorEvent e, CancellationToken ct)
        {
            Interlocked.Increment(ref CallCount);
            return Task.CompletedTask;
        }
    }

    public sealed class FanOutEvent : IIntegrationEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();
        public string EventType { get; init; } = "fanout.event.v1";
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }

    public sealed class FirstFanOutHandler : IIntegrationEventHandler<FanOutEvent>
    {
        public static int CallCount;
        public Task HandleAsync(FanOutEvent e, CancellationToken ct)
        {
            Interlocked.Increment(ref CallCount);
            return Task.CompletedTask;
        }
    }

    public sealed class SecondFanOutHandler : IIntegrationEventHandler<FanOutEvent>
    {
        public static int CallCount;
        public Task HandleAsync(FanOutEvent e, CancellationToken ct)
        {
            Interlocked.Increment(ref CallCount);
            return Task.CompletedTask;
        }
    }
}
