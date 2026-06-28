using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Events.Abstractions;
using Modulus.Events.Extensions;
using Modulus.Inbox;
using Modulus.Inbox.Abstractions;
using Modulus.Inbox.Extensions;
using Xunit;

namespace Modulus.Inbox.Tests;

[Trait("Category", "Unit")]
public sealed class InboxDecoratorTests
{
    [Fact]
    public async Task AddInbox_DecoratesHandler_AndDispatchesWithoutThrowing()
    {
        // Reproduces the CRITICAL defect: AddInbox used to resolve the inner
        // handler via GetRequiredService(ImplementationType), which threw
        // because handlers are registered only as IIntegrationEventHandler<T>.
        // Every integration event therefore threw at dispatch time. This test
        // proves the decorator now resolves and dispatches end-to-end.
        DecoratorHandler.CallCount = 0;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<IdempotentHandlerTests.TestDbContext>(
            o => o.UseInMemoryDatabase("inbox-decorator-" + Guid.NewGuid()));
        services.AddModulusEvents(typeof(InboxDecoratorTests).Assembly);
        services.AddInbox<IdempotentHandlerTests.TestDbContext>();

        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var ssp = scope.ServiceProvider;

        var handler = ssp.GetRequiredService<IIntegrationEventHandler<DecoratorEvent>>();

        // The resolved handler must be the idempotent decorator, not the raw
        // DecoratorHandler.
        handler.Should().BeOfType<IdempotentIntegrationEventHandler<DecoratorEvent>>();

        var @event = new DecoratorEvent();

        // Dispatch must complete without throwing (the old bug).
        await handler.HandleAsync(@event, default);

        // The inner handler actually ran exactly once...
        DecoratorHandler.CallCount.Should().Be(1);

        // ...and the decorator recorded a Processed inbox row keyed by EventId.
        var db = ssp.GetRequiredService<IdempotentHandlerTests.TestDbContext>();
        var inbox = db.Set<InboxMessage>().Single(m => m.Id == @event.EventId);
        inbox.Status.Should().Be(InboxStatus.Processed);
    }

    // ── Test doubles ─────────────────────────────────────────────
    public sealed class DecoratorEvent : IIntegrationEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public string EventType { get; } = "decorator.event.v1";
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
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
}
