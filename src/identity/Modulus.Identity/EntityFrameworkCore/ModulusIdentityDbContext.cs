namespace Modulus.Identity.EntityFrameworkCore;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;

/// <summary>
/// Combined DbContext for ASP.NET Identity + OpenIddict entities.
/// Derive from this in your application to add module-specific DbSets.
/// </summary>
public class ModulusIdentityDbContext<TUser, TRole>(
    DbContextOptions options,
    ICurrentTenant currentTenant)
    : IdentityDbContext<TUser, TRole, Guid,
        IdentityUserClaim<Guid>,
        IdentityUserRole<Guid>,
        IdentityUserLogin<Guid>,
        IdentityRoleClaim<Guid>,
        IdentityUserToken<Guid>>(options)
    where TUser : ModulusUser
    where TRole : ModulusRole
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<TUser>(b =>
        {
            b.HasIndex(u => u.TenantId);
            b.HasIndex(u => u.Email);
            // TenantId is nullable (a host-level account belongs to no
            // tenant), so this can't use IHasTenantId / ModuleDbContext's
            // filter (which assumes a non-nullable TenantId) as-is. Same
            // fail-closed shape: the host sees everyone; a resolved tenant
            // sees only its own rows; an unresolved tenant (IsHost false,
            // TenantId null — a missing header, a background job that never
            // established one) sees nothing, including host-level accounts
            // (TenantId null never equals a non-null currentTenant.TenantId).
            b.HasQueryFilter(u =>
                currentTenant.IsHost
                || (currentTenant.TenantId != null && u.TenantId == currentTenant.TenantId));
        });

        builder.Entity<TRole>(b =>
        {
            b.HasIndex(r => r.TenantId);
            b.HasIndex(r => r.NormalizedName);
            b.HasQueryFilter(r =>
                currentTenant.IsHost
                || (currentTenant.TenantId != null && r.TenantId == currentTenant.TenantId));
        });

        // Use OpenIddict EF Core tables
        builder.UseOpenIddict();
    }
}

/// <summary>
/// Convenience base for the standard ModulusUser/ModulusRole pair.
/// </summary>
public class ModulusIdentityDbContext(
    DbContextOptions options,
    ICurrentTenant currentTenant)
    : ModulusIdentityDbContext<ModulusUser, ModulusRole>(options, currentTenant);
