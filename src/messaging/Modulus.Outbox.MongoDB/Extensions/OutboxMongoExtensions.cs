namespace Modulus.Outbox.MongoDB.Extensions;

using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Events.Abstractions;
using Modulus.Outbox.Abstractions;
using Modulus.Outbox.MongoDB;

public static class OutboxMongoExtensions
{
    /// <summary>
    /// Registers the MongoDB-backed outbox writer, dispatcher, and polling
    /// processor. Pass the same <see cref="IMongoDatabase"/> that your module
    /// uses. Registering an <see cref="IOutboxWriter"/> is what enables
    /// transactional enqueue in EF-backed modules; for MongoDB-only apps the
    /// writer serves direct <c>IOutboxWriter.WriteAsync</c> callers.
    /// </summary>
    public static IServiceCollection AddMongoOutbox(
        this IServiceCollection services,
        string collectionName = "outbox",
        Action<OutboxOptions>? configure = null)
    {
        var opts = new OutboxOptions();
        configure?.Invoke(opts);
        // Merge into one options instance (AddOptions chains) so multiple
        // AddMongoOutbox / AddOutbox calls don't overwrite each other.
        services.AddOptions<OutboxOptions>().Configure(o => configure?.Invoke(o));

        services.AddSingleton(sp =>
        {
            var db = sp.GetRequiredService<IMongoDatabase>();
            return db.GetCollection<MongoOutboxMessage>(collectionName);
        });

        // MongoOutboxWriter is stateless; IMongoCollection is a singleton and
        // ICurrentTenant reads a static AsyncLocal, so scoped is correct and
        // matches the EF Core writer's lifetime.
        services.AddScoped<IOutboxWriter, MongoOutboxWriter>();

        // Default dispatcher (in-process IModuleBus). Registered as TryAdd so
        // a host that wires its own IOutboxDispatcher takes precedence.
        services.TryAddScoped<IOutboxDispatcher, MongoOutboxDispatcher>();

        // Processor: scoped (one per polling iteration).
        services.AddScoped<MongoOutboxProcessor>();

        // Polling hosted service (AddHostedService is TryAddEnumerable — one
        // registration even when called multiple times). DisableAutoPolling is
        // honoured at runtime by MongoOutboxPollingService.
        services.AddHostedService<MongoOutboxPollingService>();

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
