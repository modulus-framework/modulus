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
    /// uses. Replaces the default <see cref="NullIntegrationEventOutbox"/>
    /// (registered by <c>AddModuleDatabase</c>) so that
    /// <c>ModuleDbContext.SaveChangesAsync</c> enqueues integration events to
    /// the MongoDB outbox collection.
    /// </summary>
    public static IServiceCollection AddMongoOutbox(
        this IServiceCollection services,
        string collectionName = "outbox",
        Action<OutboxOptions>? configure = null)
    {
        var opts = new OutboxOptions();
        configure?.Invoke(opts);
        services.AddSingleton(Options.Create(opts));

        services.AddSingleton(sp =>
        {
            var db = sp.GetRequiredService<IMongoDatabase>();
            return db.GetCollection<MongoOutboxMessage>(collectionName);
        });

        // MongoOutboxWriter is stateless; IMongoCollection is a singleton and
        // ICurrentTenant reads a static AsyncLocal, so scoped is correct and
        // matches the EF Core writer's lifetime.
        services.AddScoped<IOutboxWriter, MongoOutboxWriter>();

        // Replace the NullIntegrationEventOutbox (registered by
        // AddModuleDatabase) so ModuleDbContext enqueues integration events
        // to MongoDB instead of the no-op.
        services.Replace(ServiceDescriptor.Scoped<
            IIntegrationEventOutbox, MongoOutboxWriter>());

        // Default dispatcher (in-process IModuleBus). Registered as TryAdd so
        // a host that wires its own IOutboxDispatcher takes precedence.
        services.TryAddScoped<IOutboxDispatcher, MongoOutboxDispatcher>();

        // Processor: scoped (one per polling iteration).
        services.AddScoped<MongoOutboxProcessor>();

        // Polling hosted service (unless the host wants to drive the loop
        // itself).
        if (!opts.DisableAutoPolling)
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
