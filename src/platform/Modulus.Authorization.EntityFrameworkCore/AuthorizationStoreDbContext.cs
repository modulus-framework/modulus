using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Organization;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Outbox.Abstractions;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// EF Core context that owns the framework's authorization tables: permission
/// grants, the organizational hierarchy and placements, per-tenant feature
/// entitlements, and delegations. This is a <b>framework-level</b> context,
/// registered only through <see cref="IDbContextFactory{TContext}"/> (never as
/// <see cref="DbContext"/>), so it does not join the module transaction fan-out
/// or the module migration loop — its schema is initialised separately via
/// <c>MigrateAuthorizationStoreAsync</c>.
/// </summary>
public class AuthorizationStoreDbContext(
    DbContextOptions<AuthorizationStoreDbContext> options,
    ICurrentTenant currentTenant)
    : DbContext(options)
{
    internal DbSet<PermissionGrantRow> Grants => Set<PermissionGrantRow>();
    internal DbSet<OrgUnitRow> OrgUnits => Set<OrgUnitRow>();
    internal DbSet<OrgUnitParentRow> OrgUnitParents => Set<OrgUnitParentRow>();
    internal DbSet<OrgPlacementRow> OrgPlacements => Set<OrgPlacementRow>();
    internal DbSet<PlanFeatureRow> PlanFeatures => Set<PlanFeatureRow>();
    internal DbSet<TenantPlanRow> TenantPlans => Set<TenantPlanRow>();
    internal DbSet<FeatureOverrideRow> FeatureOverrides => Set<FeatureOverrideRow>();
    internal DbSet<DelegationRow> Delegations => Set<DelegationRow>();

    /// <summary>
    /// Durable audit-event outbox (auth blueprint §5.14/§16), written by
    /// <c>EfAuthorizationAuditWriter</c> and drained by
    /// <c>AuthorizationAuditRelayService</c>. Deliberately its own table rather
    /// than sharing a module's <c>outbox_messages</c> — this context stays out
    /// of the module transaction fan-out (see class remarks), so its outbox
    /// needs its own dedicated relay rather than riding <c>OutboxProcessor</c>'s
    /// scan of bare-registered <see cref="DbContext"/>s.
    /// </summary>
    internal DbSet<OutboxMessage> AuditOutbox => Set<OutboxMessage>();

    internal DbSet<RecertificationCampaignRow> RecertificationCampaigns => Set<RecertificationCampaignRow>();
    internal DbSet<RecertificationItemRow> RecertificationItems => Set<RecertificationItemRow>();

    // ── SaveChanges sync override ──────────────────────────────────
    // Intentionally not implemented: the tenant auto-stamp below only runs
    // from the async override. Allowing sync SaveChanges would let a caller
    // silently bypass it, misfiling a tenant-scoped row as host/global data
    // (TenantId left at Guid.Empty). No store in this project calls the sync
    // overload today; this just makes that a compile-time-safe invariant.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
        => throw new NotSupportedException(
            "AuthorizationStoreDbContext requires SaveChangesAsync(). " +
            "Sync SaveChanges() bypasses the tenant auto-stamp. " +
            "Call SaveChangesAsync() instead.");

    public override int SaveChanges()
        => throw new NotSupportedException(
            "AuthorizationStoreDbContext requires SaveChangesAsync(). " +
            "Sync SaveChanges() bypasses the tenant auto-stamp. " +
            "Call SaveChangesAsync() instead.");

    /// <inheritdoc />
    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken ct = default)
    {
        // Auto-stamp TenantId on newly added rows when a resolved tenant is in
        // scope. Host-context inserts (IsHost = true) skip this — they
        // represent cross-tenant / global data that must NOT carry a tenant
        // id. Entity code that explicitly sets TenantId before SaveChanges is
        // also safe: the guard only fires when the value is still Guid.Empty.
        // Mirrors ModuleDbContext.ApplyAuditFields' identical block exactly.
        if (!currentTenant.IsHost && currentTenant.TenantId is { } tenantId)
        {
            foreach (var entry in ChangeTracker.Entries<IHasTenantId>()
                         .Where(e => e.State == EntityState.Added
                                     && e.Entity.TenantId == default))
            {
                entry.Entity.TenantId = tenantId;
            }
        }

        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // One grant per (tenant, holder, permission): re-granting the same
        // triple within a tenant replaces the Allow/Deny type, mirroring the
        // in-memory store's bucket[permission] = grant upsert semantics. The
        // same role/holder name in two different tenants is a distinct row.
        var grant = modelBuilder.Entity<PermissionGrantRow>();
        grant.ToTable("ModulusPermissionGrants");
        grant.HasKey(g => new { g.TenantId, g.HolderType, g.Holder, g.Permission });
        grant.Property(g => g.Holder).HasMaxLength(256);
        grant.Property(g => g.Permission).HasMaxLength(256);

        var unit = modelBuilder.Entity<OrgUnitRow>();
        unit.ToTable("ModulusOrgUnits");
        unit.HasKey(u => u.Id);
        unit.HasIndex(u => u.TenantId);

        var edge = modelBuilder.Entity<OrgUnitParentRow>();
        edge.ToTable("ModulusOrgUnitParents");
        edge.HasKey(e => new { e.TenantId, e.ChildId, e.ParentId });

        // One placement per (tenant, user, unit): re-placing updates the
        // traversal mode.
        var placement = modelBuilder.Entity<OrgPlacementRow>();
        placement.ToTable("ModulusOrgPlacements");
        placement.HasKey(p => new { p.TenantId, p.UserId, p.OrgUnitId });

        var planFeature = modelBuilder.Entity<PlanFeatureRow>();
        planFeature.ToTable("ModulusPlanFeatures");
        planFeature.HasKey(p => new { p.Plan, p.Feature });
        planFeature.Property(p => p.Plan).HasMaxLength(128);
        planFeature.Property(p => p.Feature).HasMaxLength(256);

        var tenantPlan = modelBuilder.Entity<TenantPlanRow>();
        tenantPlan.ToTable("ModulusTenantPlans");
        tenantPlan.HasKey(t => t.TenantId);
        tenantPlan.Property(t => t.Plan).HasMaxLength(128);

        var featureOverride = modelBuilder.Entity<FeatureOverrideRow>();
        featureOverride.ToTable("ModulusFeatureOverrides");
        featureOverride.HasKey(o => new { o.TenantId, o.Feature });
        featureOverride.Property(o => o.Feature).HasMaxLength(256);

        var delegation = modelBuilder.Entity<DelegationRow>();
        delegation.ToTable("ModulusDelegations");
        delegation.HasKey(d => d.Id);
        delegation.HasIndex(d => new { d.TenantId, d.ToUserId });

        var auditOutbox = modelBuilder.Entity<OutboxMessage>();
        auditOutbox.ToTable("ModulusAuthorizationAuditOutbox");
        auditOutbox.HasKey(m => m.Id);
        auditOutbox.Property(m => m.MessageType).HasMaxLength(256);
        auditOutbox.HasIndex(m => new { m.ProcessedAt, m.NextAttemptAt });

        var campaign = modelBuilder.Entity<RecertificationCampaignRow>();
        campaign.ToTable("ModulusRecertificationCampaigns");
        campaign.HasKey(c => c.Id);
        campaign.HasMany(c => c.Items)
            .WithOne(i => i.Campaign)
            .HasForeignKey(i => i.CampaignId);
        campaign.HasIndex(c => c.CompletedAt);

        var item = modelBuilder.Entity<RecertificationItemRow>();
        item.ToTable("ModulusRecertificationItems");
        item.HasKey(i => i.Id);
        item.HasIndex(i => new { i.CampaignId, i.Decision });
        item.HasIndex(i => new { i.CampaignId, i.UserId });

        ApplyTenantFilters(modelBuilder);
    }

    /// <summary>
    /// Tenant isolation for grants, org structure, and delegations (auth
    /// blueprint gap — see plan item B4). Mirrors ModuleDbContext's
    /// IHasTenantId filter exactly: a row is visible only when the context
    /// is the host (ICurrentTenant.IsHost — multi-tenancy off, or an
    /// explicit Change(null) scope) OR its TenantId matches a *resolved*
    /// tenant; an unresolved tenant sees nothing. Feature entitlements
    /// (TenantPlanRow/FeatureOverrideRow) are already correctly tenant-keyed
    /// by explicit parameter and don't need this — see EfFeatureEntitlementStore.
    /// </summary>
    private void ApplyTenantFilters(ModelBuilder mb)
    {
        mb.Entity<PermissionGrantRow>().HasQueryFilter(e =>
            currentTenant.IsHost
            || (currentTenant.TenantId != null && e.TenantId == currentTenant.TenantId));
        mb.Entity<OrgUnitRow>().HasQueryFilter(e =>
            currentTenant.IsHost
            || (currentTenant.TenantId != null && e.TenantId == currentTenant.TenantId));
        mb.Entity<OrgUnitParentRow>().HasQueryFilter(e =>
            currentTenant.IsHost
            || (currentTenant.TenantId != null && e.TenantId == currentTenant.TenantId));
        mb.Entity<OrgPlacementRow>().HasQueryFilter(e =>
            currentTenant.IsHost
            || (currentTenant.TenantId != null && e.TenantId == currentTenant.TenantId));
        mb.Entity<DelegationRow>().HasQueryFilter(e =>
            currentTenant.IsHost
            || (currentTenant.TenantId != null && e.TenantId == currentTenant.TenantId));
    }
}

/// <summary>Row backing a <see cref="PermissionGrant"/>.</summary>
internal sealed class PermissionGrantRow : IHasTenantId
{
    public Guid TenantId { get; set; }
    public GrantHolderType HolderType { get; set; }
    public string Holder { get; set; } = null!;
    public string Permission { get; set; } = null!;
    public PermissionGrantType Type { get; set; }
}

/// <summary>A node of the organizational hierarchy.</summary>
internal sealed class OrgUnitRow : IHasTenantId
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
}

/// <summary>A child→parent edge; several rows per child model a matrixed DAG.</summary>
internal sealed class OrgUnitParentRow : IHasTenantId
{
    public Guid TenantId { get; set; }
    public Guid ChildId { get; set; }
    public Guid ParentId { get; set; }
}

/// <summary>Row backing an <see cref="OrgPlacement"/>.</summary>
internal sealed class OrgPlacementRow : IHasTenantId
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid OrgUnitId { get; set; }
    public OrgScopeMode Mode { get; set; }
}

/// <summary>A feature bundled into a named plan.</summary>
internal sealed class PlanFeatureRow
{
    public string Plan { get; set; } = null!;
    public string Feature { get; set; } = null!;
}

/// <summary>The plan a tenant is assigned to.</summary>
internal sealed class TenantPlanRow
{
    public Guid TenantId { get; set; }
    public string Plan { get; set; } = null!;
}

/// <summary>A per-tenant feature override (force-on add-on / force-off block).</summary>
internal sealed class FeatureOverrideRow
{
    public Guid TenantId { get; set; }
    public string Feature { get; set; } = null!;
    public bool Enabled { get; set; }
}

/// <summary>
/// Row backing a <see cref="Modulus.Authorization.Governance.Delegation"/>.
/// Role and permission sets are stored as JSON arrays; validity is evaluated
/// in memory via <c>Delegation.IsActiveAt</c> so decision-time semantics are
/// identical across database providers (SQLite stores DateTimeOffset as text,
/// where SQL comparison across offsets is unreliable).
/// </summary>
internal sealed class DelegationRow : IHasTenantId
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid FromUserId { get; set; }
    public string FromRolesJson { get; set; } = "[]";
    public Guid ToUserId { get; set; }
    public string PermissionsJson { get; set; } = "[]";
    public DateTimeOffset NotBefore { get; set; }
    public DateTimeOffset NotAfter { get; set; }
    public bool Revoked { get; set; }
}
