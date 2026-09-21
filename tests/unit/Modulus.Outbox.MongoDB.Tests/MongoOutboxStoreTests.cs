namespace Modulus.Outbox.MongoDB.Tests;

using FluentAssertions;
using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

[Trait("Category", "Integration")]
public sealed class MongoOutboxStoreTests : IAsyncLifetime
{
    private MongoDbContainer _container = null!;
    private IMongoClient _client = null!;

    public async Task InitializeAsync()
    {
        _container = new MongoDbBuilder("mongo:7.0").Build();
        await _container.StartAsync();
        _client = new MongoClient(_container.GetConnectionString());
    }

    public async Task DisposeAsync() => await _container.DisposeAsync().AsTask();

    [Fact]
    public async Task MongoConnection_CanInsertOutboxMessage()
    {
        var db = _client.GetDatabase("test_db");
        var collection = db.GetCollection<BsonDocument>("outbox_messages");

        var doc = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() },
            { "aggregate_id", Guid.NewGuid().ToString() },
            { "event_type", "test.event" },
            { "created_at", DateTime.UtcNow }
        };
        await collection.InsertOneAsync(doc);

        var result = await collection.FindAsync(new BsonDocument());
        var docs = await result.ToListAsync();
        docs.Should().HaveCount(1);
        docs[0]["event_type"].Should().Be("test.event");
    }

    [Fact]
    public async Task MongoConnection_CanUpdateDocument()
    {
        var db = _client.GetDatabase("test_db");
        var collection = db.GetCollection<BsonDocument>("outbox_messages2");

        var id = ObjectId.GenerateNewId();
        var doc = new BsonDocument { { "_id", id }, { "status", "pending" } };
        await collection.InsertOneAsync(doc);

        var update = Builders<BsonDocument>.Update.Set("status", "published");
        await collection.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", id), update);

        var result = await collection.FindAsync(Builders<BsonDocument>.Filter.Eq("_id", id));
        var updated = await result.FirstOrDefaultAsync();
        updated!["status"].Should().Be("published");
    }
}
