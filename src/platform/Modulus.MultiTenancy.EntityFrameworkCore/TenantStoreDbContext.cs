using Microsoft.EntityFrameworkCore;

namespace Modulus.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// EF Core context that owns the framework's tenant, membership and data-store tables, generic over its entity types the way
/// <c>IdentityDbContext&lt;TUser, TRole, ...&gt;</c> is. An application that needs more columns derives its own entity classes
/// from <see cref="TenantEntity"/>, <see cref="TenantMembershipEntity"/> and <see cref="TenantDataStoreEntity"/>, derives a
/// context from this one, and registers it with <c>AddEfCoreTenantStore&lt;TContext, TTenant, TMembership, TDataStore&gt;</c>.
/// The default, non-generic <see cref="TenantStoreDbContext"/> uses the framework's entities.
/// </summary>
/// <remarks>
/// This is a <b>framework-level</b> context, intentionally registered only as itself (never as <see cref="DbContext"/>), so it
/// does not join the module transaction fan-out or the module migration loop. Its schema is initialised separately via
/// <c>MigrateTenantStoreAsync</c>.
/// </remarks>
/// <typeparam name="TTenant">The tenant entity (a company).</typeparam>
/// <typeparam name="TMembership">The membership entity (who may enter which company).</typeparam>
/// <typeparam name="TDataStore">The tenant data-store entity (where a company's database secret lives).</typeparam>
public class TenantStoreDbContext<TTenant, TMembership, TDataStore>(DbContextOptions options)
    : DbContext(options)
    where TTenant : TenantEntity
    where TMembership : TenantMembershipEntity
    where TDataStore : TenantDataStoreEntity
{
    public DbSet<TTenant> Tenants => Set<TTenant>();

    public DbSet<TMembership> TenantMemberships => Set<TMembership>();

    public DbSet<TDataStore> TenantDataStores => Set<TDataStore>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var tenant = modelBuilder.Entity<TTenant>();
        tenant.ToTable("ModulusTenants");
        tenant.HasKey(t => t.Id);
        tenant.Property(t => t.Slug).IsRequired().HasMaxLength(128);
        tenant.HasIndex(t => t.Slug).IsUnique();
        tenant.Property(t => t.DisplayName).HasMaxLength(256);
        tenant.HasIndex(t => t.GroupId);

        var membership = modelBuilder.Entity<TMembership>();
        membership.ToTable("ModulusTenantMemberships");
        membership.HasKey(m => new { m.UserId, m.TenantId });
        membership.HasIndex(m => m.TenantId);

        // A store row is a pointer to a secret, never a connection string.
        var store = modelBuilder.Entity<TDataStore>();
        store.ToTable("ModulusTenantDataStores");
        store.HasKey(s => new { s.TenantId, s.StoreKey });
        store.Property(s => s.StoreKey).IsRequired().HasMaxLength(32);
        store.Property(s => s.ProviderKey).IsRequired().HasMaxLength(32);
        store.Property(s => s.SecretPath).IsRequired().HasMaxLength(256);
        store.Property(s => s.Isolation).HasConversion<string>().HasMaxLength(32);
        store.Property(s => s.Status).HasConversion<string>().HasMaxLength(32);
    }
}

/// <summary>The tenant store with the framework's own entities. Its migrations live in the generated tenancy module.</summary>
public class TenantStoreDbContext(DbContextOptions<TenantStoreDbContext> options)
    : TenantStoreDbContext<TenantEntity, TenantMembershipEntity, TenantDataStoreEntity>(options);
