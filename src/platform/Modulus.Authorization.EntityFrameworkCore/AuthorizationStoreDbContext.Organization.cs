using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// The authorization context with application-defined organization entities, the way <c>IdentityDbContext&lt;TUser, TRole, ...&gt;</c>
/// is extended. An application derives its own org unit, org unit parent, org unit profile and company profile classes (adding
/// columns), derives a context from this one, and registers it through <c>AddEfCoreAuthorizationStores&lt;TContext, ...&gt;</c>.
/// The default <see cref="AuthorizationStoreDbContext"/> uses the framework's own entities.
/// </summary>
/// <typeparam name="TOrgUnit">The organizational unit row.</typeparam>
/// <typeparam name="TOrgUnitParent">The child-to-parent edge row.</typeparam>
/// <typeparam name="TOrgUnitProfile">The organizational unit profile row.</typeparam>
/// <typeparam name="TCompanyProfile">The company profile row (one per company).</typeparam>
public class AuthorizationStoreDbContext<TOrgUnit, TOrgUnitParent, TOrgUnitProfile, TCompanyProfile>(
    DbContextOptions options,
    ICurrentTenant currentTenant)
    : AuthorizationStoreDbContextBase(options, currentTenant)
    where TOrgUnit : OrgUnitRow, new()
    where TOrgUnitParent : OrgUnitParentRow, new()
    where TOrgUnitProfile : OrgUnitProfileRow, new()
    where TCompanyProfile : CompanyProfileRow, new()
{
    internal DbSet<TOrgUnit> OrgUnits => Set<TOrgUnit>();

    internal DbSet<TOrgUnitParent> OrgUnitParents => Set<TOrgUnitParent>();

    internal DbSet<TOrgUnitProfile> OrgUnitProfiles => Set<TOrgUnitProfile>();

    internal DbSet<TCompanyProfile> CompanyProfiles => Set<TCompanyProfile>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var unit = modelBuilder.Entity<TOrgUnit>();
        unit.ToTable("ModulusOrgUnits");
        unit.HasKey(u => u.Id);
        unit.HasIndex(u => u.TenantId);

        var edge = modelBuilder.Entity<TOrgUnitParent>();
        edge.ToTable("ModulusOrgUnitParents");
        edge.HasKey(e => new { e.TenantId, e.ChildId, e.ParentId });

        // One placement per (tenant, user, unit): re-placing updates the
        var unitProfile = modelBuilder.Entity<TOrgUnitProfile>();
        unitProfile.ToTable("ModulusOrgUnitProfiles");
        unitProfile.HasKey(p => new { p.TenantId, p.UnitId });
        unitProfile.Property(p => p.Code).HasMaxLength(64);
        unitProfile.Property(p => p.NormalizedCode).HasMaxLength(64);
        unitProfile.Property(p => p.Name).HasMaxLength(256);
        unitProfile.Property(p => p.Kind).HasMaxLength(64);
        unitProfile.HasIndex(p => new { p.TenantId, p.NormalizedCode }).IsUnique();
        var company = modelBuilder.Entity<TCompanyProfile>();
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


        ApplyOrganizationTenantFilters(modelBuilder);
    }

    /// <summary>
    /// Tenant isolation for the organization structure and company profiles: the same rule as the base filters (host context
    /// sees every row, a resolved tenant sees its own, an unresolved tenant sees nothing).
    /// </summary>
    private void ApplyOrganizationTenantFilters(ModelBuilder mb)
    {
        mb.Entity<TOrgUnit>().HasQueryFilter(e =>
            CurrentTenant.IsHost
            || (CurrentTenant.TenantId != null && e.TenantId == CurrentTenant.TenantId));
        mb.Entity<TOrgUnitParent>().HasQueryFilter(e =>
            CurrentTenant.IsHost
            || (CurrentTenant.TenantId != null && e.TenantId == CurrentTenant.TenantId));
        mb.Entity<TOrgUnitProfile>().HasQueryFilter(e =>
            CurrentTenant.IsHost
            || (CurrentTenant.TenantId != null && e.TenantId == CurrentTenant.TenantId));
        mb.Entity<TCompanyProfile>().HasQueryFilter(e =>
            CurrentTenant.IsHost
            || (CurrentTenant.TenantId != null && e.TenantId == CurrentTenant.TenantId));
    }
}

/// <summary>The authorization context with the framework's own organization entities.</summary>
public class AuthorizationStoreDbContext(DbContextOptions<AuthorizationStoreDbContext> options, ICurrentTenant currentTenant)
    : AuthorizationStoreDbContext<OrgUnitRow, OrgUnitParentRow, OrgUnitProfileRow, CompanyProfileRow>(options, currentTenant);
