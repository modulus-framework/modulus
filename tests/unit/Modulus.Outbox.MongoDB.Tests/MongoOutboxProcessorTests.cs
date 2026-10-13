namespace Modulus.Outbox.MongoDB.Tests;

using FluentAssertions;
using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modulus.Outbox.Abstractions;
using Testcontainers.MongoDb;
using Xunit;

/// <summary>
/// Runs <see cref="MongoOutboxProcessor"/> against a real MongoDB (Docker required).
/// </summary>
[Trait("Category", "Integration")]
public sealed class MongoOutboxProcessorTests : IAsyncLifetime
{
    private MongoDbContainer _container = null!;
    private IMongoCollection<MongoOutboxMessage> _collection = null!;

    public async Task InitializeAsync()
    {
        _container = new MongoDbBuilder("mongo:7.0").Build();
        await _container.StartAsync();
        _collection = new MongoClient(_container.GetConnectionString())
            .GetDatabase("outbox_tests")
            .GetCollection<MongoOutboxMessage>("outbox_messages");
    }

    public async Task DisposeAsync() => await _container.DisposeAsync().AsTask();

    [Fact]
    public async Task ProcessAsync_dispatches_and_marks_the_messages_processed()
    {
        var dispatcher = new RecordingDispatcher(_ => Task.CompletedTask);
        await SeedAsync(2);

        await CreateProcessor(dispatcher, new OutboxOptions()).ProcessAsync();

        dispatcher.Calls.Should().Be(2);
        (await ReadAllAsync()).Should().AllSatisfy(m => m.ProcessedAt.Should().NotBeNull());
    }

    [Fact]
    public async Task ProcessAsync_stops_before_the_claim_expires_and_releases_the_rest()
    {
        // With a 5s claim the batch must stop 1s before it expires. A first dispatch that takes
        // longer than that ends the batch and hands the remaining documents back unlocked, so a
        // peer that reclaims them cannot dispatch them a second time.
        var dispatcher = new RecordingDispatcher(m => m.Payload == "{\"n\":1}" ? Task.Delay(4500) : Task.CompletedTask);
        await SeedAsync(3);

        await CreateProcessor(dispatcher, new OutboxOptions { LockTimeoutSec = 5 }).ProcessAsync();

        dispatcher.Calls.Should().Be(1, "the claim was about to expire after the first dispatch");
        var rows = await ReadAllAsync();
        rows.Single(r => r.Payload == "{\"n\":1}").ProcessedAt.Should().NotBeNull();
        rows.Where(r => r.Payload != "{\"n\":1}").Should().HaveCount(2).And.AllSatisfy(r =>
        {
            r.ProcessedAt.Should().BeNull();
            r.RetryCount.Should().Be(0, "an undispatched message burns no retry budget");
            r.LockedBy.Should().BeNull();
            r.LockedUntil.Should().BeNull();
        });
    }

    [Fact]
    public async Task The_poller_drains_a_backlog_larger_than_one_batch_without_waiting_for_the_interval()
    {
        // Five messages in batches of two: a poller that sleeps the 10-minute interval after each batch would dispatch only two.
        var dispatcher = new RecordingDispatcher(_ => Task.CompletedTask);
        await SeedAsync(5);
        // The processor reads its batch size from the container, so the same options must be registered there.
        var options = Options.Create(new OutboxOptions { BatchSize = 2, PollingIntervalSec = 600 });
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IOptions<OutboxOptions>>(options)
            .AddSingleton<IMongoCollection<MongoOutboxMessage>>(_collection)
            .AddSingleton<IOutboxDispatcher>(dispatcher)
            .AddScoped<MongoOutboxProcessor>()
            .BuildServiceProvider();
        var poller = new MongoOutboxPollingService(services, options, NullLogger<MongoOutboxPollingService>.Instance);

        await poller.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => dispatcher.Calls == 5, TimeSpan.FromSeconds(20));
        }
        finally
        {
            await poller.StopAsync(CancellationToken.None);
        }

        dispatcher.Calls.Should().Be(5);
        (await ReadAllAsync()).Should().AllSatisfy(m => m.ProcessedAt.Should().NotBeNull());
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(20);
    }

    private MongoOutboxProcessor CreateProcessor(IOutboxDispatcher dispatcher, OutboxOptions options)
    {
        var services = new ServiceCollection()
            .AddSingleton(dispatcher)
            .BuildServiceProvider();
        return new MongoOutboxProcessor(
            _collection, services, Options.Create(options), NullLogger<MongoOutboxProcessor>.Instance);
    }

    private async Task SeedAsync(int count)
    {
        var start = DateTime.UtcNow.AddSeconds(-count);
        for (var n = 1; n <= count; n++)
        {
            await _collection.InsertOneAsync(new MongoOutboxMessage
            {
                MessageType = "test.event",
                Payload = $"{{\"n\":{n}}}",
                ModuleName = "Tests",
                CreatedAt = start.AddSeconds(n),
            });
        }
    }

    private Task<List<MongoOutboxMessage>> ReadAllAsync() =>
        _collection.Find(FilterDefinition<MongoOutboxMessage>.Empty).ToListAsync();

    private sealed class RecordingDispatcher(Func<OutboxMessage, Task> onDispatch) : IOutboxDispatcher
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public async Task DispatchAsync(OutboxMessage message, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            await onDispatch(message);
        }
    }
}
