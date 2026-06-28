namespace Modulus.Inbox.MongoDB.Extensions;

using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Inbox.Abstractions;
using Modulus.Inbox.Extensions;
using Modulus.Inbox.MongoDB;

public static class InboxMongoExtensions
{
    /// <summary>
    /// Registers the MongoDB-backed inbox store and wraps all
    /// <c>IIntegrationEventHandler{T}</c> registrations with the idempotent
    /// decorator — providing the same dedup guarantees as the EF Core inbox.
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

        // Ensure unique index on _id (which is the integration event EventId)
        services.AddHostedService<InboxIndexInitializer>();

        // Decorate all IIntegrationEventHandler<T> registrations with the
        // idempotent decorator — the piece that was MISSING, leaving the
        // MongoDB inbox completely unwired.
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
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to create inbox indexes");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
