namespace Modulus.Outbox.Integration.Tests;

using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modulus.Inbox;
using Modulus.Inbox.Abstractions;
using Modulus.Outbox.Abstractions;
using Xunit;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OutboxInboxPostgresTests(PostgresFixture fixture)
{
    [Fact]
    public async Task TwoProcessors_RacingOverTheSameRows_DispatchEachMessageExactlyOnce()
    {
        var module = "race-" + Guid.NewGuid().ToString("N");
        const int count = 300;
        await using (var seed = fixture.CreateContext())
        {
            for (var i = 0; i < count; i++)
                seed.Set<OutboxMessage>().Add(NewMessage(module, i));
            await seed.SaveChangesAsync();
        }

        var dispatched = new ConcurrentBag<Guid>();
        var dispatcher = new RecordingDispatcher(dispatched, module);
        var options = Options.Create(new OutboxOptions { BatchSize = 25 });
        var first = BuildProcessor(dispatcher, options);
        var second = BuildProcessor(dispatcher, options);

        // Each processor keeps polling until a pass finds nothing left, as the polling service would.
        await Task.WhenAll(Drain(first), Drain(second));

        var mine = dispatched.Where(id => dispatcher.Ids.Contains(id)).ToList();
        mine.Should().HaveCount(count);
        mine.Distinct().Should().HaveCount(count, "a claimed row must never be dispatched by the other instance");

        await using var check = fixture.CreateContext();
        (await check.Set<OutboxMessage>().CountAsync(m => m.ModuleName == module && m.ProcessedAt == null))
            .Should().Be(0);
    }

    [Fact]
    public async Task RowWithExpiredLock_IsReclaimedAndDispatched()
    {
        var module = "expired-" + Guid.NewGuid().ToString("N");
        await using (var seed = fixture.CreateContext())
        {
            var msg = NewMessage(module, 0);
            msg.LockedBy = "crashed";
            msg.LockedUntil = DateTime.UtcNow.AddMinutes(-5);
            seed.Set<OutboxMessage>().Add(msg);
            await seed.SaveChangesAsync();
        }

        var dispatched = new ConcurrentBag<Guid>();
        var dispatcher = new RecordingDispatcher(dispatched, module);
        await Drain(BuildProcessor(dispatcher, Options.Create(new OutboxOptions())));

        dispatcher.Ids.Should().HaveCount(1);
    }

    [Fact]
    public async Task ConcurrentInboxClaims_ForTheSameEvent_LetExactlyOneConsumerWin()
    {
        var eventId = Guid.NewGuid();
        const int consumers = 8;
        using var gate = new Barrier(consumers);

        var results = await Task.WhenAll(Enumerable.Range(0, consumers).Select(_ => Task.Run(async () =>
        {
            await using var db = fixture.CreateContext();
            var store = new EfInboxStore(db);
            gate.SignalAndWait();
            try
            {
                return await store.TryClaimAsync(
                    eventId, "Handler", "type", "{}", maxRetries: 5, claimTimeout: TimeSpan.FromMinutes(1), CancellationToken.None);
            }
            catch (InboxDeferralException)
            {
                return null; // the loser of the primary-key race defers (it is redelivered later)
            }
        })));

        results.Count(r => r is not null).Should().Be(1);
    }

    private static OutboxMessage NewMessage(string module, int n) => new()
    {
        MessageType = "Test.Event, Test",
        Payload = $"{{\"n\":{n}}}",
        ModuleName = module,
        CreatedAt = DateTime.UtcNow.AddMilliseconds(n),
    };

    private OutboxProcessor BuildProcessor(IOutboxDispatcher dispatcher, IOptions<OutboxOptions> options)
    {
        var services = new ServiceCollection();
        services.AddDbContext<MessagingDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<MessagingDbContext>());
        services.AddSingleton(dispatcher);
        return new OutboxProcessor(services.BuildServiceProvider(), options, NullLogger<OutboxProcessor>.Instance);
    }

    private static async Task Drain(OutboxProcessor processor)
    {
        do
        {
            await processor.ProcessAsync();
        }
        while (processor.HasMoreWork);
    }

    private sealed class RecordingDispatcher(ConcurrentBag<Guid> all, string module) : IOutboxDispatcher
    {
        public ConcurrentBag<Guid> Ids { get; } = [];

        public Task DispatchAsync(OutboxMessage message, CancellationToken ct)
        {
            if (message.ModuleName == module)
            {
                all.Add(message.Id);
                Ids.Add(message.Id);
            }
            return Task.CompletedTask;
        }
    }
}
