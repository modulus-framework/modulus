namespace Modulus.Inbox.MongoDB.Extensions;

using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulus.Inbox.Abstractions;
using Modulus.Inbox.Extensions;
using Modulus.Inbox.MongoDB;

public static class InboxMongoExtensions
{
    /// <summary>
    /// Registers the MongoDB-backed inbox store and the dispatch-time
    /// idempotent decorator — providing the same dedup guarantees as the EF
    /// Core inbox.
    /// </summary>
    public static IServiceCollection AddMongoInbox(
        this IServiceCollection services,
        string collectionName = "inbox",
        Action<InboxOptions>? configure = null)
    {
        services.AddOptions<InboxOptions>()
            .Configure(opts => configure?.Invoke(opts));

        services.AddSingleton(sp =>
        {
            var db = sp.GetRequiredService<IMongoDatabase>();
            return db.GetCollection<MongoInboxMessage>(collectionName);
        });

        services.AddScoped<IInboxStore, MongoInboxStore>();

        // Ensure the unique compound index on (EventId, HandlerName) that
        // gives claims their atomicity — see MongoInboxStore's doc comment.
        services.AddHostedService<InboxIndexInitializer>();

        // Registers the dispatch-time handler decorator — see
        // DecorateIntegrationEventHandlers' remarks (Modulus.Inbox) for why
        // this replaced mutating IIntegrationEventHandler{T} registrations.
        return services.DecorateIntegrationEventHandlers();
    }
}

internal sealed class InboxIndexInitializer(
    IMongoCollection<MongoInboxMessage> collection,
    ILogger<InboxIndexInitializer> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            var indexes = await collection.Indexes.ListAsync(ct);
            var existing = await indexes.ToListAsync(ct);

            var hasStatusIndex = existing.Any(i =>
                i.Contains("name") && i["name"].AsString == "ix_status_retry");
            if (!hasStatusIndex)
            {
                var model = new CreateIndexModel<MongoInboxMessage>(
                    Builders<MongoInboxMessage>.IndexKeys
                        .Ascending(x => x.Status)
                        .Ascending(x => x.RetryCount),
                    new CreateIndexOptions { Name = "ix_status_retry" });
                await collection.Indexes.CreateOneAsync(model, cancellationToken: ct);
            }

            // Gives (EventId, HandlerName) the same atomic-claim guarantee the
            // collection's implicit _id index used to give (EventId) alone,
            // back when one event could only ever have one handler's row.
            // Partial: legacy docs (written before EventId existed) all share
            // the same missing-field value and would collide with EACH OTHER
            // under a plain unique index — the partial filter excludes any
            // document that doesn't have EventId set, so only current-schema
            // docs (which always set it) are covered.
            var hasClaimIndex = existing.Any(i =>
                i.Contains("name") && i["name"].AsString == "ux_eventid_handlername");
            if (!hasClaimIndex)
            {
                var model = new CreateIndexModel<MongoInboxMessage>(
                    Builders<MongoInboxMessage>.IndexKeys
                        .Ascending(x => x.EventId)
                        .Ascending(x => x.HandlerName),
                    new CreateIndexOptions<MongoInboxMessage>
                    {
                        Name = "ux_eventid_handlername",
                        Unique = true,
                        PartialFilterExpression = Builders<MongoInboxMessage>.Filter.Exists(x => x.EventId),
                    });
                await collection.Indexes.CreateOneAsync(model, cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to create inbox indexes");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
