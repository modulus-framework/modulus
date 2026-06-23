namespace Modulus.Outbox.MongoDB;

using System.Text.Json;
using global::MongoDB.Driver;
using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;
using Modulus.Outbox.Abstractions;

/// <summary>
/// MongoDB document for the outbox collection.
/// </summary>
public sealed class MongoOutboxMessage
{
    public Guid      Id            { get; init; } = Guid.NewGuid();
    public string    MessageType   { get; init; } = default!;
    public string    Payload       { get; init; } = default!;
    public Guid      TenantId      { get; init; }
    public string    ModuleName    { get; init; } = default!;
    public DateTime  CreatedAt     { get; init; } = DateTime.UtcNow;
    public DateTime? ProcessedAt   { get; set;  }
    public int       RetryCount    { get; set;  }
    public string?   Error         { get; set;  }
    public string?   CorrelationId { get; init; }
    public string?   CausationId   { get; init; }
}

/// <summary>
/// IOutboxWriter implementation backed by MongoDB.
/// Does NOT support transactions — use only with MongoDB's eventual consistency.
/// </summary>
internal sealed class MongoOutboxWriter(
    IMongoCollection<MongoOutboxMessage> collection,
    ICurrentTenant tenant)
    : IOutboxWriter
{
    public Task WriteAsync<TEvent>(
        TEvent @event,
        CancellationToken ct = default)
        where TEvent : IIntegrationEvent
    {
        var doc = new MongoOutboxMessage
        {
            MessageType   = @event.GetType().AssemblyQualifiedName!,
            Payload       = JsonSerializer.Serialize(@event, @event.GetType()),
            TenantId      = tenant.TenantId ?? Guid.Empty,
            ModuleName    = typeof(TEvent).Module.Name.Replace(".dll", ""),
            CausationId   = @event.EventId.ToString(),
        };

        return collection.InsertOneAsync(doc, cancellationToken: ct);
    }
}
