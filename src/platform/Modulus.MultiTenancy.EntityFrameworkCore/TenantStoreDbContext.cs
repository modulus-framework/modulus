using Microsoft.EntityFrameworkCore;

namespace Modulus.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// EF Core context that owns the framework's tenant table. This is a
/// <b>framework-level</b> context, intentionally registered only as itself (never
/// as <see cref="DbContext"/>), so it does not join the module transaction fan-out
/// or the module migration loop — its schema is initialised separately via
/// <c>MigrateTenantStoreAsync</c>.
/// </summary>
public class TenantStoreDbContext(DbContextOptions<TenantStoreDbContext> options)
    : DbContext(options)
{
    public DbSet<TenantEntity> Tenants => Set<TenantEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var tenant = modelBuilder.Entity<TenantEntity>();
        tenant.ToTable("ModulusTenants");
        tenant.HasKey(t => t.Id);
        tenant.Property(t => t.Slug).IsRequired().HasMaxLength(128);
        tenant.HasIndex(t => t.Slug).IsUnique();
        tenant.Property(t => t.DisplayName).HasMaxLength(256);
    }
}
