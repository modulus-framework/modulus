namespace Modulus.Inbox.MongoDB.Tests;

using FluentAssertions;
using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

[Trait("Category", "Unit")]
public sealed class MongoInboxStoreTests : IAsyncLifetime
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
    public async Task MongoConnection_CanInsertDocument()
    {
        var db = _client.GetDatabase("test_db");
        var collection = db.GetCollection<BsonDocument>("test_collection");

        var doc = new BsonDocument { { "_id", ObjectId.GenerateNewId() }, { "name", "test" } };
        await collection.InsertOneAsync(doc);

        var result = await collection.FindAsync(new BsonDocument());
        var docs = await result.ToListAsync();
        docs.Should().HaveCount(1);
        docs[0]["name"].Should().Be("test");
    }

    [Fact]
    public async Task MongoConnection_CanQueryDocument()
    {
        var db = _client.GetDatabase("test_db");
        var collection = db.GetCollection<BsonDocument>("test_collection2");

        var doc1 = new BsonDocument { { "_id", "doc1" }, { "value", 100 } };
        await collection.InsertOneAsync(doc1);

        var result = await collection.FindAsync(Builders<BsonDocument>.Filter.Eq("_id", "doc1"));
        var found = await result.FirstOrDefaultAsync();
        found.Should().NotBeNull();
        found!["value"].Should().Be(100);
    }
}
