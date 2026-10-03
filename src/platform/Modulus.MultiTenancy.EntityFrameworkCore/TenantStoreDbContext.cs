using Microsoft.EntityFrameworkCore;

namespace Modulus.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// EF Core context that owns the framework's tenant and tenant-membership tables. This is a
/// <b>framework-level</b> context, intentionally registered only as itself (never
/// as <see cref="DbContext"/>), so it does not join the module transaction fan-out
/// or the module migration loop — its schema is initialised separately via
/// <c>MigrateTenantStoreAsync</c>.
/// </summary>
public class TenantStoreDbContext(DbContextOptions<TenantStoreDbContext> options)
    : DbContext(options)
{
    public DbSet<TenantEntity> Tenants => Set<TenantEntity>();

    public DbSet<TenantMembershipEntity> TenantMemberships => Set<TenantMembershipEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var tenant = modelBuilder.Entity<TenantEntity>();
        tenant.ToTable("ModulusTenants");
        tenant.HasKey(t => t.Id);
        tenant.Property(t => t.Slug).IsRequired().HasMaxLength(128);
        tenant.HasIndex(t => t.Slug).IsUnique();
        tenant.Property(t => t.DisplayName).HasMaxLength(256);
        tenant.HasIndex(t => t.GroupId);

        var membership = modelBuilder.Entity<TenantMembershipEntity>();
        membership.ToTable("ModulusTenantMemberships");
        membership.HasKey(m => new { m.UserId, m.TenantId });
        membership.HasIndex(m => m.TenantId);
    }
}
