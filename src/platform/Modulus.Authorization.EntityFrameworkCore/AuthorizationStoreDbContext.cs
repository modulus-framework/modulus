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
    internal DbSet<ScopedGrantRow> ScopedGrants => Set<ScopedGrantRow>();
    internal DbSet<AssignmentRow> Assignments => Set<AssignmentRow>();
    internal DbSet<ApprovalAuthorityRow> ApprovalAuthorities => Set<ApprovalAuthorityRow>();
    internal DbSet<SodRuleRow> SodRules => Set<SodRuleRow>();
    internal DbSet<RoleInclusionRow> RoleInclusions => Set<RoleInclusionRow>();
    internal DbSet<PositionRow> Positions => Set<PositionRow>();
    internal DbSet<PartyLinkRow> PartyLinks => Set<PartyLinkRow>();
    internal DbSet<PartyCeilingRow> PartyCeilings => Set<PartyCeilingRow>();
    internal DbSet<PositionAssignmentRow> PositionAssignments => Set<PositionAssignmentRow>();
    internal DbSet<AccessRequestRow> AccessRequests => Set<AccessRequestRow>();
    internal DbSet<OrgUnitProfileRow> OrgUnitProfiles => Set<OrgUnitProfileRow>();
    internal DbSet<CompanyProfileRow> CompanyProfiles => Set<CompanyProfileRow>();

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

        // Grants that carry a scope, a validity window or a restriction live beside the plain grants: the plain table's key
        // (tenant, holder, permission) allows one row per permission, and widening it would break every existing database.
        var scoped = modelBuilder.Entity<ScopedGrantRow>();
        scoped.ToTable("ModulusScopedGrants");
        scoped.HasKey(g => g.Id);
        scoped.Property(g => g.Holder).HasMaxLength(256);
        scoped.Property(g => g.Permission).HasMaxLength(256);
        scoped.Property(g => g.Scope).HasMaxLength(300);
        scoped.Property(g => g.Reason).HasMaxLength(1000);
        scoped.HasIndex(g => new { g.TenantId, g.HolderType, g.Holder, g.Permission, g.Type, g.Scope }).IsUnique();

        var assignment = modelBuilder.Entity<AssignmentRow>();
        assignment.ToTable("ModulusAssignments");
        assignment.HasKey(a => new { a.TenantId, a.UserId, a.AssignmentType, a.TargetId });
        assignment.Property(a => a.AssignmentType).HasMaxLength(128);

        var approval = modelBuilder.Entity<ApprovalAuthorityRow>();
        approval.ToTable("ModulusApprovalAuthorities");
        approval.HasKey(a => a.Id);
        approval.Property(a => a.Holder).HasMaxLength(256);
        approval.Property(a => a.Permission).HasMaxLength(256);
        approval.Property(a => a.MaxAmount).HasPrecision(18, 4);
        approval.Property(a => a.Currency).HasMaxLength(8);
        approval.Property(a => a.DocumentType).HasMaxLength(256);
        approval.HasIndex(a => new { a.TenantId, a.Permission, a.HolderType, a.Holder });

        var position = modelBuilder.Entity<PositionRow>();
        position.ToTable("ModulusPositions");
        position.HasKey(p => p.Id);
        position.Property(p => p.Code).HasMaxLength(100);
        position.Property(p => p.NormalizedCode).HasMaxLength(100);
        position.Property(p => p.Name).HasMaxLength(200);
        position.Property(p => p.Roles).HasMaxLength(4000);
        position.HasIndex(p => new { p.TenantId, p.NormalizedCode }).IsUnique();

        var holding = modelBuilder.Entity<PositionAssignmentRow>();
        holding.ToTable("ModulusPositionAssignments");
        holding.HasKey(p => p.Id);
        holding.HasIndex(p => new { p.TenantId, p.UserId });
        holding.HasIndex(p => new { p.TenantId, p.PositionId });

        var partyLink = modelBuilder.Entity<PartyLinkRow>();
        partyLink.ToTable("ModulusPartyLinks");
        partyLink.HasKey(p => new { p.TenantId, p.UserId });
        partyLink.Property(p => p.Kind).HasMaxLength(64);
        partyLink.HasIndex(p => new { p.TenantId, p.Kind, p.PartyId });

        var partyCeiling = modelBuilder.Entity<PartyCeilingRow>();
        partyCeiling.ToTable("ModulusPartyCeilings");
        partyCeiling.HasKey(p => new { p.TenantId, p.Kind, p.Permission });
        partyCeiling.Property(p => p.Kind).HasMaxLength(64);
        partyCeiling.Property(p => p.Permission).HasMaxLength(256);

        var inclusion = modelBuilder.Entity<RoleInclusionRow>();
        inclusion.ToTable("ModulusRoleInclusions");
        inclusion.HasKey(r => new { r.TenantId, r.NormalizedRole, r.NormalizedIncludes });
        inclusion.Property(r => r.Role).HasMaxLength(256);
        inclusion.Property(r => r.NormalizedRole).HasMaxLength(256);
        inclusion.Property(r => r.Includes).HasMaxLength(256);
        inclusion.Property(r => r.NormalizedIncludes).HasMaxLength(256);

        var sodRule = modelBuilder.Entity<SodRuleRow>();
        sodRule.ToTable("ModulusSodRules");
        sodRule.HasKey(r => r.Id);
        sodRule.Property(r => r.Name).HasMaxLength(200);
        sodRule.Property(r => r.NormalizedName).HasMaxLength(200);
        sodRule.Property(r => r.Permissions).HasMaxLength(4000);
        sodRule.Property(r => r.Rationale).HasMaxLength(2000);
        sodRule.HasIndex(r => new { r.TenantId, r.NormalizedName }).IsUnique();

        var accessRequest = modelBuilder.Entity<AccessRequestRow>();
        accessRequest.ToTable("ModulusAccessRequests");
        accessRequest.HasKey(r => r.Id);
        accessRequest.Property(r => r.Permissions).HasMaxLength(4000);
        accessRequest.Property(r => r.Reason).HasMaxLength(2000);
        accessRequest.Property(r => r.Note).HasMaxLength(2000);
        accessRequest.HasIndex(r => new { r.TenantId, r.Status, r.RequesterId });

        var unitProfile = modelBuilder.Entity<OrgUnitProfileRow>();
        unitProfile.ToTable("ModulusOrgUnitProfiles");
        unitProfile.HasKey(p => new { p.TenantId, p.UnitId });
        unitProfile.Property(p => p.Code).HasMaxLength(64);
        unitProfile.Property(p => p.NormalizedCode).HasMaxLength(64);
        unitProfile.Property(p => p.Name).HasMaxLength(256);
        unitProfile.Property(p => p.Kind).HasMaxLength(64);
        unitProfile.HasIndex(p => new { p.TenantId, p.NormalizedCode }).IsUnique();

        var company = modelBuilder.Entity<CompanyProfileRow>();
        company.ToTable("ModulusCompanyProfiles");
        company.HasKey(c => c.TenantId);
        company.Property(c => c.LegalName).HasMaxLength(256);
        company.Property(c => c.TradeName).HasMaxLength(256);
        company.Property(c => c.RegistrationNumber).HasMaxLength(64);
        company.Property(c => c.TaxId).HasMaxLength(64);
        company.Property(c => c.Address).HasMaxLength(1000);
        company.Property(c => c.Country).HasMaxLength(2);
        company.Property(c => c.Currency).HasMaxLength(3);
        company.Property(c => c.TimeZone).HasMaxLength(64);
        company.Property(c => c.Language).HasMaxLength(16);

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
        mb.Entity<ScopedGrantRow>().HasQueryFilter(e =>
            currentTenant.IsHost
            || (currentTenant.TenantId != null && e.TenantId == currentTenant.TenantId));
        mb.Entity<AssignmentRow>().HasQueryFilter(e =>
            currentTenant.IsHost
            || (currentTenant.TenantId != null && e.TenantId == currentTenant.TenantId));
        mb.Entity<ApprovalAuthorityRow>().HasQueryFilter(e =>
            currentTenant.IsHost
            || (currentTenant.TenantId != null && e.TenantId == currentTenant.TenantId));
        mb.Entity<RoleInclusionRow>().HasQueryFilter(e =>
            currentTenant.IsHost
            || (currentTenant.TenantId != null && e.TenantId == currentTenant.TenantId));
        mb.Entity<PartyLinkRow>().HasQueryFilter(e =>
            currentTenant.IsHost
            || (currentTenant.TenantId != null && e.TenantId == currentTenant.TenantId));
        mb.Entity<PartyCeilingRow>().HasQueryFilter(e =>
            currentTenant.IsHost
            || (currentTenant.TenantId != null && e.TenantId == currentTenant.TenantId));
        mb.Entity<PositionRow>().HasQueryFilter(e =>
            currentTenant.IsHost
            || (currentTenant.TenantId != null && e.TenantId == currentTenant.TenantId));
        mb.Entity<PositionAssignmentRow>().HasQueryFilter(e =>
            currentTenant.IsHost
            || (currentTenant.TenantId != null && e.TenantId == currentTenant.TenantId));
        mb.Entity<SodRuleRow>().HasQueryFilter(e =>
            currentTenant.IsHost
            || (currentTenant.TenantId != null && e.TenantId == currentTenant.TenantId));
        mb.Entity<AccessRequestRow>().HasQueryFilter(e =>
            currentTenant.IsHost
            || (currentTenant.TenantId != null && e.TenantId == currentTenant.TenantId));
        mb.Entity<OrgUnitProfileRow>().HasQueryFilter(e =>
            currentTenant.IsHost
            || (currentTenant.TenantId != null && e.TenantId == currentTenant.TenantId));
        mb.Entity<CompanyProfileRow>().HasQueryFilter(e =>
            currentTenant.IsHost
            || (currentTenant.TenantId != null && e.TenantId == currentTenant.TenantId));
    }
}

/// <summary>Row backing a scoped, temporary or restricting <see cref="PermissionGrant"/>.</summary>
internal sealed class ScopedGrantRow : IHasTenantId
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public GrantHolderType HolderType { get; set; }
    public string Holder { get; set; } = null!;
    public string Permission { get; set; } = null!;
    public PermissionGrantType Type { get; set; }

    /// <summary><see cref="Modulus.Authorization.Scopes.PermissionScope.Format"/>; never null (tenant-wide is <c>tenant</c>).</summary>
    public string Scope { get; set; } = "tenant";
    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public string? Reason { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Row backing an <see cref="Modulus.Authorization.Scopes.Assignment"/>.</summary>
internal sealed class AssignmentRow : IHasTenantId
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string AssignmentType { get; set; } = null!;
    public Guid TargetId { get; set; }
    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
}

/// <summary>Row backing an <see cref="Modulus.Authorization.Approval.ApprovalAuthority"/>.</summary>
internal sealed class ApprovalAuthorityRow : IHasTenantId
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public GrantHolderType HolderType { get; set; }
    public string Holder { get; set; } = null!;
    public string Permission { get; set; } = null!;
    public decimal MaxAmount { get; set; }
    public string? Currency { get; set; }
    public string? DocumentType { get; set; }
    public Guid? OrgUnitId { get; set; }
    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Row backing an <see cref="AccessRequest"/>.</summary>
internal sealed class AccessRequestRow : IHasTenantId
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public AccessRequestKind Kind { get; set; }
    public Guid RequesterId { get; set; }
    public string Permissions { get; set; } = "[]";
    public string Reason { get; set; } = null!;
    public int Hours { get; set; }
    public AccessRequestStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset? AccessEndsAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}

/// <summary>Row backing an <see cref="OrgUnitProfile"/>.</summary>
internal sealed class OrgUnitProfileRow : IHasTenantId
{
    public Guid TenantId { get; set; }
    public Guid UnitId { get; set; }
    public string Code { get; set; } = null!;
    public string NormalizedCode { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public bool IsClosed { get; set; }
    public Guid? ManagerUserId { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Row backing a <see cref="CompanyProfile"/>; one per company.</summary>
internal sealed class CompanyProfileRow : IHasTenantId
{
    public Guid TenantId { get; set; }
    public string LegalName { get; set; } = null!;
    public string? TradeName { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? TaxId { get; set; }
    public string? Address { get; set; }
    public string? Country { get; set; }
    public string? Currency { get; set; }
    public int FiscalYearStartMonth { get; set; } = 1;
    public string? TimeZone { get; set; }
    public string? Language { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
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

/// <summary>Row backing a role inclusion: <see cref="Role"/> holds everything <see cref="Includes"/> holds.</summary>
internal sealed class RoleInclusionRow : IHasTenantId
{
    public Guid TenantId { get; set; }
    public string Role { get; set; } = null!;
    public string NormalizedRole { get; set; } = null!;
    public string Includes { get; set; } = null!;
    public string NormalizedIncludes { get; set; } = null!;
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Row backing a stored <see cref="Modulus.Authorization.Governance.SodConstraint"/>.</summary>
internal sealed class SodRuleRow : IHasTenantId
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = null!;
    public string NormalizedName { get; set; } = null!;

    /// <summary>The mutually exclusive permissions, as a JSON array.</summary>
    public string Permissions { get; set; } = "[]";
    public string? Rationale { get; set; }
    public bool IsEnabled { get; set; } = true;
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Row backing a position: a post (optionally in one org unit) that grants roles to whoever holds it.</summary>
internal sealed class PositionRow : IHasTenantId
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Code { get; set; } = null!;
    public string NormalizedCode { get; set; } = null!;
    public string Name { get; set; } = null!;
    public Guid? OrgUnitId { get; set; }

    /// <summary>The roles the position grants, as a JSON array.</summary>
    public string Roles { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Row backing the holding of a position by a user for a period.</summary>
internal sealed class PositionAssignmentRow : IHasTenantId
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PositionId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Row linking an account to an external party (buyer, supplier, subcontractor).</summary>
internal sealed class PartyLinkRow : IHasTenantId
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Kind { get; set; } = null!;
    public Guid PartyId { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Row allowing a party kind to use a permission (or a prefix ending in a star).</summary>
internal sealed class PartyCeilingRow : IHasTenantId
{
    public Guid TenantId { get; set; }
    public string Kind { get; set; } = null!;
    public string Permission { get; set; } = null!;
}
