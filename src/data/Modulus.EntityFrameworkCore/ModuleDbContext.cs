namespace Modulus.EntityFrameworkCore;

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Domain;
using Modulus.Core.Abstractions.Entities;
using Modulus.EntityFrameworkCore.Abstractions;
using Modulus.Events;
using Modulus.Events.Abstractions;
using Modulus.Outbox.Abstractions;

/// <summary>
/// Base DbContext for all Modulus modules.
/// Extend this and set TablePrefix to isolate your module tables.
/// </summary>
public abstract class ModuleDbContext(
    DbContextOptions options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider sp)
    : DbContext(options), IUnitOfWork
{
    /// <summary>Prefix applied to all table names. e.g. "cat_"</summary>
    protected abstract string TablePrefix { get; }

    // ── IUnitOfWork ───────────────────────────────────────────────
    public Task<int> CommitAsync(CancellationToken ct = default)
        => SaveChangesAsync(ct);

    // ── SaveChangesAsync override ─────────────────────────────────
    public override async Task<int> SaveChangesAsync(
        CancellationToken ct = default)
    {
        ApplyAuditFields();
        var domainEvents = CollectDomainEvents();

        // Enqueue integration events to this context's outbox table BEFORE
        // SaveChanges so the outbox row(s) participate in the same DB
        // transaction as the domain writes — closing the dual-write gap.
        // We add directly to THIS context's Set<OutboxMessage>() rather than
        // going through IIntegrationEventOutbox, which resolved the wrong
        // DbContext in multi-module apps (the last-registered one).
        // The outbox is opt-in: only enqueue when AddOutbox was called
        // (IOutboxWriter is registered).
        if (sp.GetService<IOutboxWriter>() is not null)
        {
            foreach (var integrationEvent in domainEvents
                         .OfType<IIntegrationEvent>())
            {
                Set<OutboxMessage>().Add(new OutboxMessage
                {
                    MessageType = integrationEvent.GetType().AssemblyQualifiedName!,
                    Payload = System.Text.Json.JsonSerializer.Serialize(
                        integrationEvent, integrationEvent.GetType()),
                    TenantId = currentTenant.TenantId ?? Guid.Empty,
                    ModuleName = GetType().Name.Replace("DbContext", string.Empty),
                    CausationId = integrationEvent.EventId.ToString(),
                });
            }
        }

        var result = await base.SaveChangesAsync(ct);
        await dispatcher.DispatchAsync(domainEvents, ct);
        return result;
    }

    // ── Model configuration ───────────────────────────────────────
    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);
        ConfigureOutbox(mb);
        ApplyTablePrefix(mb);
        ApplyQueryFilters(mb);
    }

    /// <summary>
    /// Maps the <see cref="OutboxMessage"/> entity so that outbox rows share
    /// the module's DbContext and participate in the same transaction.
    /// Configured before <see cref="ApplyTablePrefix"/> so the table gets the
    /// module prefix (e.g. <c>cat_outbox_messages</c>).
    /// </summary>
    private static void ConfigureOutbox(ModelBuilder mb)
    {
        mb.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(x => x.Id);
            b.Property(x => x.MessageType).HasMaxLength(500).IsRequired();
            b.Property(x => x.Payload).IsRequired();
            b.Property(x => x.ModuleName).HasMaxLength(100);
            b.HasIndex(x => new { x.ProcessedAt, x.LockedUntil, x.RetryCount });
            b.HasIndex(x => new { x.ProcessedAt, x.CreatedAt });
            b.HasIndex(x => x.TenantId);
        });
    }

    // ── Private helpers ───────────────────────────────────────────
    private void ApplyTablePrefix(ModelBuilder mb)
    {
        foreach (var entity in mb.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table is not null && !table.StartsWith(TablePrefix,
                    StringComparison.OrdinalIgnoreCase))
                entity.SetTableName(TablePrefix + table);
        }
    }

    /// <summary>
    /// Registers global query filters. EF Core allows only ONE query filter
    /// per entity, so entities implementing both <see cref="ISoftDelete"/> and
    /// <see cref="IHasTenantId"/> get a single combined predicate (the previous
    /// separate-registration logic silently dropped soft-delete for them).
    ///
    /// The tenant predicate captures the <c>currentTenant</c> service FIELD
    /// (never a local value), so EF Core re-evaluates it against the current
    /// DbContext instance on every query. Capturing a value
    /// (<c>var tid = currentTenant.TenantId</c>) would freeze the tenant into
    /// the cached model — the first request's tenant would then gate every
    /// subsequent request, a silent cross-tenant leak. We also register the
    /// filter unconditionally (no <c>IsAvailable</c> short-circuit at
    /// model-build time); when no tenant is in scope the predicate degrades to
    /// match-all rather than filtering on <c>Guid.Empty</c>.
    /// </summary>
    private void ApplyQueryFilters(ModelBuilder mb)
    {
        foreach (var entity in mb.Model.GetEntityTypes())
        {
            var clr = entity.ClrType;
            var soft = typeof(ISoftDelete).IsAssignableFrom(clr);
            var ten = typeof(IHasTenantId).IsAssignableFrom(clr);
            if (!soft && !ten) continue;

            var methodName = (soft, ten) switch
            {
                (true, true) => nameof(SetTenantAndSoftDeleteFilter),
                (true, false) => nameof(SetSoftDeleteFilter),
                (false, true) => nameof(SetTenantFilter),
                _ => null!,
            };

            typeof(ModuleDbContext)
                .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(clr)
                .Invoke(this, [mb]);
        }
    }

    private void SetSoftDeleteFilter<T>(ModelBuilder mb) where T : class, ISoftDelete
        => mb.Entity<T>().HasQueryFilter(e => !e.IsDeleted);

    private void SetTenantFilter<T>(ModelBuilder mb) where T : class, IHasTenantId
        => mb.Entity<T>().HasQueryFilter(e =>
            currentTenant.TenantId == null || e.TenantId == currentTenant.TenantId);

    private void SetTenantAndSoftDeleteFilter<T>(ModelBuilder mb)
        where T : class, IHasTenantId, ISoftDelete
        => mb.Entity<T>().HasQueryFilter(e => !e.IsDeleted
            && (currentTenant.TenantId == null || e.TenantId == currentTenant.TenantId));

    private void ApplyAuditFields()
    {
        var now = DateTime.UtcNow;
        var user = currentUser.UserName ?? "system";

        // Soft-delete first so the audit loop below stamps UpdatedAt/UpdatedBy
        // on the soft-deleted row (which transitions Deleted→Modified).
        foreach (var entry in ChangeTracker.Entries<ISoftDelete>()
            .Where(e => e.State == EntityState.Deleted))
        {
            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
            entry.Entity.DeletedAt = now;
            entry.Entity.DeletedBy = user;
        }

        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = user;
            }
            if (entry.State is EntityState.Added
                             or EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = user;
            }
        }
    }

    private IReadOnlyList<IDomainEvent> CollectDomainEvents()
    {
        var aggregates = ChangeTracker
            .Entries<IAggregateRoot>()
            .Select(e => e.Entity)
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();

        var events = aggregates
            .SelectMany(a => a.DomainEvents)
            .ToList();

        aggregates.ForEach(a => a.ClearDomainEvents());
        return events;
    }
}