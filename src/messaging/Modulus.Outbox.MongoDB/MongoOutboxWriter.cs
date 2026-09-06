namespace Modulus.Outbox.MongoDB;

using System.Diagnostics;
using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
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
    public string? TraceParent { get; init; }
    public string? TraceState { get; init; }
    public int? SchemaVersion { get; init; }
}

/// <summary>
/// <see cref="IOutboxWriter"/> implementation backed by MongoDB.
///
/// ⚠️ LIMITATION: Does NOT support transactions. Domain writes and outbox
/// writes are separate operations — a domain write may succeed while the
/// outbox write fails, or vice versa. This violates the outbox pattern's
/// core guarantee: a published integration event whose domain side-effect
/// failed (rolled back), or a domain change with no corresponding outbox row.
///
/// For production use: either migrate to a relational database (EF Core)
/// which uses shared transactions, or accept eventual-consistency semantics
/// and ensure your domain logic is idempotent. The <see cref="MongoOutboxProcessor"/>
/// relays rows to the event bus at-least-once; handler idempotency is mandatory.
/// </summary>
internal sealed class MongoOutboxWriter(
    IMongoCollection<MongoOutboxMessage> collection,
    IServiceProvider sp,
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
        var activity = Activity.Current;
        var serializer = sp.GetRequiredService<IMessageSerializer>();
        return new MongoOutboxMessage
        {
            MessageType = IntegrationEventNaming.GetName(type),
            Payload = serializer.Serialize(@event, type),
            TenantId = tenant.TenantId ?? Guid.Empty,
            ModuleName = type.Module.Name.Replace(".dll", ""),
            // Resolved lazily so registration doesn't depend on the correlation
            // context being wired (matches EfOutboxWriter).
            CorrelationId = sp.GetService<ICorrelationContext>()?.CorrelationId,
            CausationId = sp.GetService<ICausationIdContext>()?.CausationId,
            TraceParent = activity?.Id,
            TraceState = activity?.TraceStateString,
        };
    }
}
