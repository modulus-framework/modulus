namespace Modulus.Outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events.Abstractions;
using Modulus.Outbox.Abstractions;

/// <summary>
/// EF Core implementation of <see cref="IOutboxWriter"/>. Adds an
/// <see cref="OutboxMessage"/> row to the scoped module
/// <see cref="DbContext"/>; the row is persisted on the next
/// <c>SaveChanges</c> in the same transaction as the domain writes.
/// </summary>
/// <remarks>
/// The primary transactional path is <c>ModuleDbContext.SaveChangesAsync</c>,
/// which enqueues integration events itself (gated on this writer being
/// registered). This class additionally serves direct
/// <c>IOutboxWriter.WriteAsync</c> callers. The target context is resolved via
/// the shared entity→context map (the seam repositories use) rather than a raw
/// <see cref="DbContext"/> resolution, which would silently return the LAST
/// registered module context and land the row in the wrong table. The map is
/// consulted lazily via <see cref="IServiceProvider"/> (not constructor
/// injection) so registration never forces a context cycle.
/// </remarks>
internal sealed class EfOutboxWriter(
    IServiceProvider sp,
    ICurrentTenant tenant)
    : IOutboxWriter
{
    private DbContext? _db;

    private DbContext Db =>
        _db ??= ResolveTargetContext();

    private DbContext ResolveTargetContext()
    {
        var contextType = sp.GetService<IEntityContextMap>()
            ?.Resolve(typeof(OutboxMessage));

        if (contextType is not null)
            return (DbContext)sp.GetRequiredService(contextType);

        // OutboxMessage is mapped into every module context (ambiguous by design)
        // so the map intentionally returns null. Use the single registered
        // context when unambiguous; otherwise fail fast — direct writes have no
        // ambient owner, unlike ModuleDbContext.SaveChangesAsync which uses its
        // own Set<OutboxMessage>() inline.
        var distinct = sp.GetServices<DbContext>()
            .GroupBy(ctx => ctx.GetType())
            .Select(g => g.First())
            .ToList();

        if (distinct.Count == 1)
            return distinct[0];

        throw new InvalidOperationException(
            $"Cannot resolve a unique DbContext for {nameof(OutboxMessage)}: " +
            $"{distinct.Count} contexts are registered. Write via the owning module's " +
            "ModuleDbContext (domain events enqueue inline) instead of direct IOutboxWriter.");
    }

    public Task WriteAsync<TEvent>(
        TEvent @event,
        CancellationToken ct = default)
        where TEvent : IIntegrationEvent
    {
        var serializer = sp.GetRequiredService<IMessageSerializer>();
        Db.Set<OutboxMessage>().Add(OutboxRowFactory.Create(
            @event,
            tenant.TenantId ?? Guid.Empty,
            Db.GetType().Name.Replace("DbContext", string.Empty),
            sp.GetService<ICorrelationContext>()?.CorrelationId,
            serializer,
            sp.GetService<ICausationIdContext>()?.CausationId));
        return Task.CompletedTask;
    }
}
