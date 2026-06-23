namespace Modulus.Outbox.MongoDB.Extensions;

using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Outbox.Abstractions;
using Modulus.Outbox.MongoDB;

public static class OutboxMongoExtensions
{
    /// <summary>
    /// Registers the MongoDB-backed outbox writer.
    /// Pass the same IMongoDatabase that your module uses.
    /// </summary>
    public static IServiceCollection AddMongoOutbox(
        this IServiceCollection services,
        string collectionName = "outbox")
    {
        services.AddSingleton(sp =>
        {
            var db = sp.GetRequiredService<IMongoDatabase>();
            return db.GetCollection<MongoOutboxMessage>(collectionName);
        });

        services.AddSingleton<IOutboxWriter, MongoOutboxWriter>();

        // Ensure TTL index for auto-cleanup of processed messages (optional)
        services.AddHostedService<OutboxIndexInitializer>();

        return services;
    }
}

internal sealed class OutboxIndexInitializer(
    IMongoCollection<MongoOutboxMessage> collection,
    ILogger<OutboxIndexInitializer> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            var indexes = await collection.Indexes.ListAsync(ct);
            var existing = await indexes.ToListAsync(ct);
            var hasProcessedIndex = existing.Any(i =>
                i.Contains("name") && i["name"].AsString == "ix_processed_at");

            if (!hasProcessedIndex)
            {
                var model = new CreateIndexModel<MongoOutboxMessage>(
                    Builders<MongoOutboxMessage>.IndexKeys
                        .Ascending(x => x.ProcessedAt)
                        .Ascending(x => x.RetryCount),
                    new CreateIndexOptions { Name = "ix_processed_at" });
                await collection.Indexes.CreateOneAsync(model, cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to create outbox indexes");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
