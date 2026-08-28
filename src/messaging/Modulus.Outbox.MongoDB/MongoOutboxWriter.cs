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
    public Guid Id { get; init; } = Guid.NewGuid();
    public string MessageType { get; init; } = default!;
    public string Payload { get; init; } = default!;
    public Guid TenantId { get; init; }
    public string ModuleName { get; init; } = default!;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public string? LockedBy { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public int RetryCount { get; set; }
    public string? Error { get; set; }
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
}

/// <summary>
/// <see cref="IOutboxWriter"/> implementation backed by MongoDB. Does NOT
/// support transactions — use only with MongoDB's eventual consistency; the
/// <see cref="MongoOutboxProcessor"/> relays rows to the event bus
/// at-least-once.
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
        => collection.InsertOneAsync(BuildDoc(@event), cancellationToken: ct);

    // ── Shared document-creation logic ────────────────────────────
    private MongoOutboxMessage BuildDoc(IIntegrationEvent @event)
    {
        var type = @event.GetType();
        return new MongoOutboxMessage
        {
            MessageType = IntegrationEventNaming.GetName(type),
            Payload = JsonSerializer.Serialize(@event, type),
            TenantId = tenant.TenantId ?? Guid.Empty,
            ModuleName = type.Module.Name.Replace(".dll", ""),
            CausationId = @event.EventId.ToString(),
        };
    }
}
