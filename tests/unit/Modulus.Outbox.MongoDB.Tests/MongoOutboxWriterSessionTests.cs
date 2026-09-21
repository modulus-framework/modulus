namespace Modulus.Outbox.MongoDB.Tests;

using global::MongoDB.Driver;
using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;
using NSubstitute;
using Xunit;

[Trait("Category", "Unit")]
public sealed class MongoOutboxWriterSessionTests
{
    private sealed record StubEvent(string EventType) : IntegrationEventBase(EventType);

    [Fact]
    public async Task WriteAsync_WithoutSessionProvider_UsesPlainInsert()
    {
        var collection = Substitute.For<IMongoCollection<MongoOutboxMessage>>();
        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(IMessageSerializer)).Returns(new StubSerializer());
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.TenantId.Returns((Guid?)null);

        var writer = new MongoOutboxWriter(collection, sp, tenant);
        await writer.WriteAsync(new StubEvent("test.stub.v1"));

        await collection.Received(1).InsertOneAsync(
            Arg.Any<MongoOutboxMessage>(),
            Arg.Any<InsertOneOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteAsync_WithAmbientSession_JoinsSessionInsert()
    {
        var collection = Substitute.For<IMongoCollection<MongoOutboxMessage>>();
        var session = Substitute.For<IClientSessionHandle>();
        var sessionProvider = Substitute.For<IMongoOutboxSessionProvider>();
        sessionProvider.CurrentSession.Returns(session);
        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(IMessageSerializer)).Returns(new StubSerializer());
        sp.GetService(typeof(IMongoOutboxSessionProvider)).Returns(sessionProvider);
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.TenantId.Returns((Guid?)null);

        var writer = new MongoOutboxWriter(collection, sp, tenant);
        await writer.WriteAsync(new StubEvent("test.stub.v1"));

        await collection.Received(1).InsertOneAsync(
            session,
            Arg.Any<MongoOutboxMessage>(),
            Arg.Any<InsertOneOptions>(),
            Arg.Any<CancellationToken>());
    }

    private sealed class StubSerializer : IMessageSerializer
    {
        public string Serialize(object payload, Type type) => "{}";
        public object? Deserialize(string json, Type type) => null;
    }
}
