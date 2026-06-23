namespace Modulus.EntityFrameworkCore;

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Domain;
using Modulus.Core.Abstractions.Entities;
using Modulus.EntityFrameworkCore.Abstractions;
using Modulus.Events;

/// <summary>
/// Base DbContext for all Modulus modules.
/// Extend this and set TablePrefix to isolate your module tables.
/// </summary>
public abstract class ModuleDbContext(
    DbContextOptions           options,
    ICurrentTenant             currentTenant,
    ICurrentUser               currentUser,
    DomainEventDispatcher      dispatcher)
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
        var result       = await base.SaveChangesAsync(ct);
        await dispatcher.DispatchAsync(domainEvents, ct);
        return result;
    }

    // ── Model configuration ───────────────────────────────────────
    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);
        ApplyTablePrefix(mb);
        ApplySoftDeleteFilter(mb);
        ApplyTenantFilter(mb);
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

    private void ApplySoftDeleteFilter(ModelBuilder mb)
    {
        foreach (var entity in mb.Model.GetEntityTypes()
            .Where(e => typeof(ISoftDelete)
                .IsAssignableFrom(e.ClrType)))
        {
            typeof(ModuleDbContext)
                .GetMethod(nameof(SetSoftDeleteFilter),
                    BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(entity.ClrType)
                .Invoke(null, [mb]);
        }
    }

    private static void SetSoftDeleteFilter<T>(
        ModelBuilder mb) where T : class, ISoftDelete
        => mb.Entity<T>().HasQueryFilter(e => !e.IsDeleted);

    private void ApplyTenantFilter(ModelBuilder mb)
    {
        if (!currentTenant.IsAvailable) return;
        foreach (var entity in mb.Model.GetEntityTypes()
            .Where(e => typeof(IHasTenantId)
                .IsAssignableFrom(e.ClrType)))
        {
            typeof(ModuleDbContext)
                .GetMethod(nameof(SetTenantFilter),
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(entity.ClrType)
                .Invoke(this, [mb]);
        }
    }

    private void SetTenantFilter<T>(
        ModelBuilder mb) where T : class, IHasTenantId
    {
        var tid = currentTenant.TenantId;
        mb.Entity<T>().HasQueryFilter(e => e.TenantId == tid);
    }

    private void ApplyAuditFields()
    {
        var now  = DateTime.UtcNow;
        var user = currentUser.UserName ?? "system";

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

        foreach (var entry in ChangeTracker.Entries<ISoftDelete>()
            .Where(e => e.State == EntityState.Deleted))
        {
            entry.State             = EntityState.Modified;
            entry.Entity.IsDeleted  = true;
            entry.Entity.DeletedAt  = now;
            entry.Entity.DeletedBy  = user;
        }
    }

    private IReadOnlyList<IDomainEvent> CollectDomainEvents()
    {
        var aggregates = ChangeTracker
            .Entries<AggregateRoot>()
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