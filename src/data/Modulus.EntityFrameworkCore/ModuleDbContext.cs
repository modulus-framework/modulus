namespace Modulus.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.DataProtection;
using Modulus.Core.Abstractions.Domain;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Null;
using Modulus.EntityFrameworkCore.Abstractions;
using Modulus.EntityFrameworkCore.DataProtection;
using Modulus.EntityFrameworkCore.ModelBuilding;
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

    private ICurrentDataScope? _dataScope;

    /// <summary>
    /// The organizational data scope for the current request, read by the org-scope
    /// query filter. Resolved lazily from this context's request container; falls
    /// back to <see cref="NullCurrentDataScope"/> (unrestricted, filter is a no-op)
    /// when organizational scoping is not configured. Referenced through the
    /// executing context so EF Core re-evaluates it per query — the same seam
    /// <c>currentTenant</c> uses — never frozen into the cached model.
    /// </summary>
    private ICurrentDataScope DataScope
        => _dataScope ??= sp.GetService<ICurrentDataScope>() ?? NullCurrentDataScope.Instance;

    // ── IUnitOfWork ───────────────────────────────────────────────
    public Task<int> CommitAsync(CancellationToken ct = default)
        => SaveChangesAsync(ct);

    // ── SaveChanges sync override ──────────────────────────────────
    // Intentionally not implemented: all framework guarantees (audit fields,
    // soft-delete conversion, outbox enqueueing, domain event dispatch) require
    // async execution. Calling sync SaveChanges bypasses all of them, risking
    // data loss (Remove() becomes hard DELETE) and silent event loss.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
        => throw new NotSupportedException(
            "ModuleDbContext requires SaveChangesAsync(). " +
            "Sync SaveChanges() bypasses audit fields, soft-delete conversion, " +
            "and outbox enqueueing. Call SaveChangesAsync() instead.");

    public override int SaveChanges()
        => throw new NotSupportedException(
            "ModuleDbContext requires SaveChangesAsync(). " +
            "Sync SaveChanges() bypasses audit fields, soft-delete conversion, " +
            "and outbox enqueueing. Call SaveChangesAsync() instead.");

    // ── SaveChangesAsync override ─────────────────────────────────
    public override async Task<int> SaveChangesAsync(
        CancellationToken ct = default)
    {
        ApplyAuditFields();
        var domainEvents = CollectDomainEvents();

        // Enqueue integration events to this context's outbox table BEFORE
        // SaveChanges so the outbox row(s) participate in the same DB
        // transaction as the domain writes — closing the dual-write gap.
        // We add directly to THIS context's Set<OutboxMessage>() so rows are
        // always written by the context that owns the domain data (never a
        // last-registered context in multi-module apps). The outbox is
        // opt-in: only enqueue when AddOutbox was called (IOutboxWriter is
        // registered).
        if (sp.GetService<IOutboxWriter>() is not null)
        {
            var correlationId = sp.GetService<ICorrelationContext>()?.CorrelationId;
            foreach (var integrationEvent in domainEvents
                         .OfType<IIntegrationEvent>())
            {
                Set<OutboxMessage>().Add(OutboxRowFactory.Create(
                    integrationEvent,
                    currentTenant.TenantId ?? Guid.Empty,
                    GetType().Name.Replace("DbContext", string.Empty),
                    correlationId));
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

        // Feature packages (Inbox, …) contribute entity mappings through this
        // seam — before prefixing so their tables get the module prefix.
        foreach (var contributor in sp.GetServices<IModuleModelContributor>())
            contributor.Contribute(mb);

        ConfigureOutbox(mb);
        ApplyTablePrefix(mb);
        ApplyQueryFilters(mb);
        ApplyPersonalDataEncryption(mb);
    }

    /// <summary>
    /// Attaches the encrypting value converter to every
    /// <see cref="ProtectedPersonalDataAttribute"/> string property when an
    /// <see cref="IPersonalDataProtector"/> is registered (i.e. the app called
    /// <c>AddModulusPersonalDataProtection</c>). Without one this is a no-op and
    /// marked columns stay plaintext, so encryption is a strictly opt-in capability.
    /// </summary>
    private void ApplyPersonalDataEncryption(ModelBuilder mb)
    {
        if (sp.GetService<IPersonalDataProtector>() is { } protector)
            mb.UseModulusPersonalDataEncryption(protector);
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
    /// Registers global query filters. EF Core allows only ONE query filter per
    /// entity, so the applicable layers — soft-delete
    /// (<see cref="ISoftDelete"/>), tenant isolation (<see cref="IHasTenantId"/>),
    /// and organizational scope (<see cref="IHasOrgUnit"/>) — are AND-combined into a
    /// single predicate (see <see cref="SetCombinedFilter{T}"/>). An entity may
    /// implement any subset; each layer it opts into is applied without dropping the
    /// others.
    ///
    /// Each layer's sub-predicate captures its service FIELD (<c>currentTenant</c>,
    /// <c>DataScope</c>) — never a local value — so EF Core re-evaluates it against
    /// the executing DbContext on every query. Capturing a value
    /// (<c>var tid = currentTenant.TenantId</c>) would freeze it into the cached
    /// model — the first request's context would then gate every subsequent request,
    /// a silent cross-tenant / cross-scope leak.
    ///
    /// Every layer is <b>fail-closed</b>. Tenant: a row is visible only when the
    /// context is the host (<see cref="ICurrentTenant.IsHost"/> — multi-tenancy off,
    /// or an explicit <c>Change(null)</c> scope) OR its tenant id matches a
    /// <i>resolved</i> tenant; an unresolved tenant sees nothing. Org scope: a row is
    /// visible only when the principal is unrestricted
    /// (<see cref="ICurrentDataScope.IsUnrestricted"/> — scoping off, or the bypass
    /// grant) OR its org unit is within the principal's resolved scope; an
    /// unauthenticated or unplaced principal (once scoping is configured) sees
    /// nothing. Seeing all tenants / all units requires a deliberate privileged scope.
    /// </summary>
    private void ApplyQueryFilters(ModelBuilder mb)
    {
        foreach (var entity in mb.Model.GetEntityTypes())
        {
            var clr = entity.ClrType;
            if (!typeof(ISoftDelete).IsAssignableFrom(clr)
                && !typeof(IHasTenantId).IsAssignableFrom(clr)
                && !typeof(IHasOrgUnit).IsAssignableFrom(clr))
                continue;

            typeof(ModuleDbContext)
                .GetMethod(nameof(SetCombinedFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(clr)
                .Invoke(this, [mb]);
        }
    }

    /// <summary>
    /// AND-combines the row-filter layers the entity opts into into the single query
    /// filter EF Core permits. Each sub-predicate is a compiler-generated lambda
    /// whose context-rooted closures EF re-evaluates per query; only the entity
    /// parameter is unified (<see cref="Combine{T}"/>), so each layer's fail-closed
    /// semantics are preserved exactly. Property access goes through
    /// <see cref="EF.Property{TProperty}(object, string)"/> so a single
    /// <c>where T : class</c> method serves every marker combination without an
    /// interface constraint.
    /// </summary>
    private void SetCombinedFilter<T>(ModelBuilder mb) where T : class
    {
        Expression<Func<T, bool>>? filter = null;

        if (typeof(ISoftDelete).IsAssignableFrom(typeof(T)))
            filter = Combine(filter, e => !EF.Property<bool>(e, nameof(ISoftDelete.IsDeleted)));

        if (typeof(IHasTenantId).IsAssignableFrom(typeof(T)))
            filter = Combine(filter, e =>
                currentTenant.IsHost
                || (currentTenant.TenantId != null
                    && EF.Property<Guid>(e, nameof(IHasTenantId.TenantId)) == currentTenant.TenantId));

        if (typeof(IHasOrgUnit).IsAssignableFrom(typeof(T)))
            filter = Combine(filter, e =>
                DataScope.IsUnrestricted
                || DataScope.OrgUnitIds.Contains(EF.Property<Guid>(e, nameof(IHasOrgUnit.OrgUnitId))));

        if (filter is not null)
            mb.Entity<T>().HasQueryFilter(filter);
    }

    /// <summary>
    /// AND-combines two entity predicates, rebinding the right lambda onto the
    /// left's parameter so the compiler-generated context closures on both sides are
    /// left intact (only the entity parameter is unified).
    /// </summary>
    private static Expression<Func<T, bool>> Combine<T>(
        Expression<Func<T, bool>>? left, Expression<Func<T, bool>> right)
    {
        if (left is null) return right;
        var parameter = left.Parameters[0];
        var rebound = new ParameterReplacer(right.Parameters[0], parameter).Visit(right.Body);
        return Expression.Lambda<Func<T, bool>>(
            Expression.AndAlso(left.Body, rebound), parameter);
    }

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }

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
