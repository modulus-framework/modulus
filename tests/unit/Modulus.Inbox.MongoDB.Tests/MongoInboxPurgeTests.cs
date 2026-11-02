namespace Modulus.Inbox.MongoDB.Tests;

using FluentAssertions;
using global::MongoDB.Driver;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modulus.Inbox.Abstractions;
using Testcontainers.MongoDb;
using Xunit;

/// <summary>
/// Housekeeping of the MongoDB inbox: processed documents past the retention window are deleted in batches; documents still in
/// flight (pending, processing, failed) keep their dedup record however old they are.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MongoInboxPurgeTests : IAsyncLifetime
{
    private MongoDbContainer _container = null!;
    private IMongoCollection<MongoInboxMessage> _collection = null!;

    public async Task InitializeAsync()
    {
        _container = new MongoDbBuilder("mongo:7.0").Build();
        await _container.StartAsync();
        var client = new MongoClient(_container.GetConnectionString());
        _collection = client.GetDatabase("inbox_purge_tests").GetCollection<MongoInboxMessage>("inbox");
    }

    public async Task DisposeAsync() => await _container.DisposeAsync().AsTask();

    [Fact]
    public async Task Purge_DeletesProcessedDocumentsOlderThanRetention_AndKeepsTheRest()
    {
        var now = DateTime.UtcNow;
        await _collection.InsertManyAsync(
        [
            Doc(InboxStatus.Processed, now.AddDays(-40)),   // purged
            Doc(InboxStatus.Processed, now.AddDays(-1)),    // kept: inside the window
            Doc(InboxStatus.Pending, now.AddDays(-40)),     // kept
            Doc(InboxStatus.Processing, now.AddDays(-40)),  // kept
            Doc(InboxStatus.Failed, now.AddDays(-40)),      // kept
        ]);

        var deleted = await Service(retentionDays: 30).PurgeAsync(CancellationToken.None);

        deleted.Should().Be(1);
        (await _collection.CountDocumentsAsync(FilterAll())).Should().Be(4);
        (await _collection.CountDocumentsAsync(Builders<MongoInboxMessage>.Filter.Eq(x => x.Status, InboxStatus.Processed))).Should().Be(1);
    }

    [Fact]
    public async Task Purge_WithRetentionZero_DeletesNothing()
    {
        await _collection.InsertOneAsync(Doc(InboxStatus.Processed, DateTime.UtcNow.AddYears(-5)));

        var deleted = await Service(retentionDays: 0).PurgeAsync(CancellationToken.None);

        deleted.Should().Be(0);
        (await _collection.CountDocumentsAsync(FilterAll())).Should().Be(1);
    }

    [Fact]
    public async Task Purge_DeletesALargeBacklogInBatches()
    {
        var old = DateTime.UtcNow.AddDays(-60);
        var docs = Enumerable.Range(0, MongoInboxPurgeService.BatchSize * 2 + 250)
            .Select(_ => Doc(InboxStatus.Processed, old))
            .ToList();
        await _collection.InsertManyAsync(docs);

        var deleted = await Service(retentionDays: 30).PurgeAsync(CancellationToken.None);

        deleted.Should().Be(docs.Count);
        (await _collection.CountDocumentsAsync(FilterAll())).Should().Be(0);
    }

    private MongoInboxPurgeService Service(int retentionDays) => new(
        _collection,
        Options.Create(new InboxOptions { ProcessedRetentionDays = retentionDays }),
        NullLogger<MongoInboxPurgeService>.Instance);

    private static FilterDefinition<MongoInboxMessage> FilterAll()
        => Builders<MongoInboxMessage>.Filter.Empty;

    private static MongoInboxMessage Doc(InboxStatus status, DateTime processedAt) => new()
    {
        Id = Guid.NewGuid(),
        EventId = Guid.NewGuid(),
        HandlerName = "Handler-" + Guid.NewGuid(),
        MessageType = "TestEvent",
        Payload = "{}",
        ModuleName = "Test",
        Status = status,
        ProcessedAt = status == InboxStatus.Processed ? processedAt : null,
        ReceivedAt = processedAt,
    };
}
