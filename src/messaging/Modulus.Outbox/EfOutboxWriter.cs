namespace Modulus.Outbox;

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;
using Modulus.Outbox.Abstractions;

/// <summary>
/// EF Core implementation of <see cref="IOutboxWriter"/> and
/// <see cref="IIntegrationEventOutbox"/>. Adds an <see cref="OutboxMessage"/>
/// row to the scoped <see cref="DbContext"/>; the row is persisted on the
/// next <c>SaveChanges</c> in the same transaction as the domain writes.
/// </summary>
/// <remarks>
/// The <see cref="DbContext"/> is resolved lazily via
/// <see cref="IServiceProvider"/> (not constructor injection) to break a
/// circular DI dependency: <c>ModuleDbContext</c> →
/// <see cref="IIntegrationEventOutbox"/> (this class) →
/// <see cref="DbContext"/> (back to <c>ModuleDbContext</c>). By the time
/// <see cref="Enqueue"/> or <see cref="WriteAsync{TEvent}"/> is called, the
/// owning <c>DbContext</c> has already been constructed and is cached in the
/// scope, so the late resolution succeeds without recursion.
/// </remarks>
internal sealed class EfOutboxWriter(
    IServiceProvider sp,
    ICurrentTenant tenant,
    IHttpContextAccessor http)
    : IOutboxWriter, IIntegrationEventOutbox
{
    private DbContext? _db;

    private DbContext Db =>
        _db ??= sp.GetRequiredService<DbContext>();

    // ── IIntegrationEventOutbox (non-generic, synchronous) ────────
    public void Enqueue(IIntegrationEvent @event) =>
        AddOutboxRow(Db, @event);

    // ── IOutboxWriter (generic, async-compat) ─────────────────────
    public Task WriteAsync<TEvent>(
        TEvent @event,
        CancellationToken ct = default)
        where TEvent : IIntegrationEvent
    {
        AddOutboxRow(Db, @event);
        return Task.CompletedTask;
    }

    // ── Shared row-creation logic ─────────────────────────────────
    private void AddOutboxRow(DbContext db, IIntegrationEvent @event)
    {
        var correlationId = http.HttpContext?
            .Request.Headers["X-Correlation-Id"]
            .FirstOrDefault();

        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            MessageType = IntegrationEventNaming.GetName(@event.GetType()),
            Payload = JsonSerializer.Serialize(@event, @event.GetType()),
            TenantId = tenant.TenantId ?? Guid.Empty,
            ModuleName = db.GetType().Name
                               .Replace("DbContext", string.Empty),
            CorrelationId = correlationId,
            CausationId = @event.EventId.ToString(),
        });
    }
}
