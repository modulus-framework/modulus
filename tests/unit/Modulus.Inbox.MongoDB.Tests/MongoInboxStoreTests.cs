namespace Modulus.Inbox.MongoDB.Tests;

using FluentAssertions;
using global::MongoDB.Driver;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.Inbox.Abstractions;
using Modulus.Inbox.MongoDB.Extensions;
using Testcontainers.MongoDb;
using Xunit;

/// <summary>
/// H16: this class used to exercise raw <c>MongoDB.Driver</c> APIs only
/// (insert a BsonDocument, query it back) and never instantiated
/// <see cref="MongoInboxStore"/> itself, so the actual Modulus type — the
/// thing that gives idempotent message processing its atomicity — had 0%
/// coverage despite having a test project. These tests build a real
/// <see cref="MongoInboxStore"/> (plus the same unique-index setup
/// <c>InboxIndexInitializer</c> creates at startup) against a
/// Testcontainers-hosted MongoDB and exercise the claim/processed/failed
/// lifecycle through <see cref="IInboxStore"/> directly.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MongoInboxStoreTests : IAsyncLifetime
{
    private MongoDbContainer _container = null!;
    private IMongoClient _client = null!;
    private IMongoCollection<MongoInboxMessage> _collection = null!;
    private IInboxStore _store = null!;

    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(5);

    public async Task InitializeAsync()
    {
        _container = new MongoDbBuilder("mongo:7.0").Build();
        await _container.StartAsync();
        _client = new MongoClient(_container.GetConnectionString());
        _collection = _client.GetDatabase("inbox_tests").GetCollection<MongoInboxMessage>("inbox");

        // Same index setup AddMongoInbox's hosted InboxIndexInitializer does
        // at startup — the unique (EventId, HandlerName) index is what gives
        // TryClaimAsync its atomicity, so a test against the store without it
        // would not actually cover the guarantee the class exists to provide.
        var initializer = new InboxIndexInitializer(_collection, NullLogger<InboxIndexInitializer>.Instance);
        await initializer.StartAsync(CancellationToken.None);

        _store = new MongoInboxStore(_collection);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync().AsTask();

    [Fact]
    public async Task TryClaimAsync_NewEvent_ReturnsAProcessingClaim()
    {
        var eventId = Guid.NewGuid();

        var claim = await _store.TryClaimAsync(
            eventId, "handler-a", "test.event", "{}", maxRetries: 3, ClaimTimeout, CancellationToken.None);

        claim.Should().NotBeNull();
        claim!.Status.Should().Be(InboxStatus.Processing);
    }

    [Fact]
    public async Task TryClaimAsync_AlreadyProcessed_ReturnsNull()
    {
        var eventId = Guid.NewGuid();
        await _store.TryClaimAsync(
            eventId, "handler-a", "test.event", "{}", maxRetries: 3, ClaimTimeout, CancellationToken.None);
        await _store.MarkProcessedAsync(eventId, "handler-a", CancellationToken.None);

        var claim = await _store.TryClaimAsync(
            eventId, "handler-a", "test.event", "{}", maxRetries: 3, ClaimTimeout, CancellationToken.None);

        claim.Should().BeNull("an already-processed event must be skipped, not reprocessed");
    }

    [Fact]
    public async Task TryClaimAsync_ConcurrentlyClaimed_ThrowsDeferral()
    {
        var eventId = Guid.NewGuid();
        await _store.TryClaimAsync(
            eventId, "handler-a", "test.event", "{}", maxRetries: 3, ClaimTimeout, CancellationToken.None);

        // Second claim attempt for the same (EventId, HandlerName) while the
        // first is still within its claim window -- must defer, not double-process.
        var act = () => _store.TryClaimAsync(
            eventId, "handler-a", "test.event", "{}", maxRetries: 3, ClaimTimeout, CancellationToken.None);

        await act.Should().ThrowAsync<InboxDeferralException>();
    }

    [Fact]
    public async Task TryClaimAsync_DifferentHandlersSameEvent_ClaimIndependently()
    {
        var eventId = Guid.NewGuid();

        var claimA = await _store.TryClaimAsync(
            eventId, "handler-a", "test.event", "{}", maxRetries: 3, ClaimTimeout, CancellationToken.None);
        var claimB = await _store.TryClaimAsync(
            eventId, "handler-b", "test.event", "{}", maxRetries: 3, ClaimTimeout, CancellationToken.None);

        claimA.Should().NotBeNull();
        claimB.Should().NotBeNull("two handlers subscribed to the same event claim independent rows");
    }

    [Fact]
    public async Task TryClaimAsync_PastRetryBudget_ReturnsNull_DeadLettered()
    {
        var eventId = Guid.NewGuid();
        const int maxRetries = 2;

        for (var i = 0; i < maxRetries; i++)
        {
            await _store.TryClaimAsync(
                eventId, "handler-a", "test.event", "{}", maxRetries, ClaimTimeout, CancellationToken.None);
            await _store.MarkFailedAsync(eventId, "handler-a", "boom", CancellationToken.None);
        }

        var claim = await _store.TryClaimAsync(
            eventId, "handler-a", "test.event", "{}", maxRetries, ClaimTimeout, CancellationToken.None);

        claim.Should().BeNull("an event that exhausted its retry budget must be dead-lettered, not reclaimed");
    }

    [Fact]
    public async Task TryClaimAsync_ExpiredClaim_IsReclaimedInsteadOfDeferringForever()
    {
        var eventId = Guid.NewGuid();
        var expiredClaimTimeout = TimeSpan.FromMilliseconds(1);

        await _store.TryClaimAsync(
            eventId, "handler-a", "test.event", "{}", maxRetries: 3, expiredClaimTimeout, CancellationToken.None);
        await Task.Delay(50);

        var reclaimed = await _store.TryClaimAsync(
            eventId, "handler-a", "test.event", "{}", maxRetries: 3, expiredClaimTimeout, CancellationToken.None);

        reclaimed.Should().NotBeNull("a claim held past its timeout means the claimant crashed and must be reclaimable");
        reclaimed!.Status.Should().Be(InboxStatus.Processing);
    }
}
