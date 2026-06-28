namespace Modulus.Identity.EntityFrameworkCore;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Modulus.Identity.Abstractions;

/// <summary>
/// Combined DbContext for ASP.NET Identity + OpenIddict entities.
/// Derive from this in your application to add module-specific DbSets.
/// </summary>
public class ModulusIdentityDbContext<TUser, TRole>(
    DbContextOptions options)
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
        });

        builder.Entity<TRole>(b =>
        {
            b.HasIndex(r => r.TenantId);
            b.HasIndex(r => r.NormalizedName);
        });

        // Use OpenIddict EF Core tables
        builder.UseOpenIddict();
    }
}

/// <summary>
/// Convenience base for the standard ModulusUser/ModulusRole pair.
/// </summary>
public class ModulusIdentityDbContext(
    DbContextOptions options)
    : ModulusIdentityDbContext<ModulusUser, ModulusRole>(options);
