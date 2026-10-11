namespace Modulus.Inbox.MongoDB.Tests;

using FluentAssertions;
using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Inbox.MongoDB;
using Modulus.Inbox.MongoDB.Extensions;
using NSubstitute;
using Xunit;

/// <summary>D2: a failed index creation stops startup, unless the application opts out of creating the indexes.</summary>
[Trait("Category", "Unit")]
public sealed class InboxIndexStartupTests
{
    private static ServiceProvider Build(IMongoDatabase database, bool skipIndexCreation)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(database);
        services.AddMongoInbox(skipIndexCreation: skipIndexCreation);
        return services.BuildServiceProvider();
    }

    private static IMongoDatabase FailingDatabase()
    {
        var database = Substitute.For<IMongoDatabase>();
        var collection = Substitute.For<IMongoCollection<MongoInboxMessage>>();
        var indexes = Substitute.For<IMongoIndexManager<MongoInboxMessage>>();
        indexes.ListAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromException<IAsyncCursor<global::MongoDB.Bson.BsonDocument>>(
            new TimeoutException("the server is unreachable")));
        collection.Indexes.Returns(indexes);
        database.GetCollection<MongoInboxMessage>(Arg.Any<string>(), Arg.Any<MongoCollectionSettings>()).Returns(collection);
        return database;
    }

    [Fact]
    public async Task A_failed_index_creation_stops_the_host_from_starting()
    {
        await using var provider = Build(FailingDatabase(), skipIndexCreation: false);

        var initializer = provider.GetServices<IHostedService>().Should().ContainSingle().Subject;

        var act = () => initializer.StartAsync(CancellationToken.None);
        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public void Skipping_index_creation_registers_no_index_step()
    {
        using var provider = Build(Substitute.For<IMongoDatabase>(), skipIndexCreation: true);

        provider.GetServices<IHostedService>().Should().BeEmpty();
    }
}
