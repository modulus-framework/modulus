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
/// Provides ambient MongoDB sessions so domain writes and outbox writes can
/// commit atomically via a multi-document transaction (requires a replica set).
/// Register an implementation and flow the session from your unit of work; when
/// absent the writer falls back to a plain insert (at-least-once, dual-write gap).
/// </summary>
public interface IMongoOutboxSessionProvider
{
    IClientSessionHandle? CurrentSession { get; }
}

/// <summary>
/// <see cref="IOutboxWriter"/> implementation backed by MongoDB.
///
/// When <see cref="IMongoOutboxSessionProvider.CurrentSession"/> is present the
/// insert joins that session's transaction (atomic with domain writes); otherwise
/// it falls back to a plain insert with an idempotent <c>Id</c> (EventId) so
/// retried writes do not duplicate. Either way consumers must dedup via inbox.
/// Row fields mirror <c>OutboxRowFactory</c> (tenant/correlation/causation/trace).
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
    {
        var doc = BuildDoc(@event);
        var session = sp.GetService<IMongoOutboxSessionProvider>()?.CurrentSession;
        return session is null
            ? collection.InsertOneAsync(doc, cancellationToken: ct)
            : collection.InsertOneAsync(session, doc, cancellationToken: ct);
    }

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
