using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Tenancy;

namespace Modulus.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// Registration + initialisation for the EF Core-backed tenant store.
/// </summary>
public static class EfTenantStoreExtensions
{
    /// <summary>
    /// Replaces the default <c>NullTenantStore</c> with the EF tenant store (the framework's own entities), and registers
    /// <see cref="TenantManager"/> for provisioning. Call <b>after</b> <c>AddMultiTenancy(...)</c>:
    /// <code>
    /// services.AddMultiTenancy(t =&gt; t.UseHeaderResolver());
    /// services.AddEfCoreTenantStore(o =&gt; o.UseNpgsql(connectionString));
    /// </code>
    /// The context is registered only as <see cref="TenantStoreDbContext"/> — never as <see cref="DbContext"/> — so it stays out
    /// of the module transaction fan-out and the module migration loop.
    /// </summary>
    public static IServiceCollection AddEfCoreTenantStore(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configure)
    {
        // The default manager is a concrete class, so the generic registration below resolves it through the base type.
        services.AddScoped<TenantManager>();
        services.AddScoped<TenantManager<TenantEntity, TenantMembershipEntity, TenantDataStoreEntity>>(
            sp => sp.GetRequiredService<TenantManager>());
        return services.AddEfCoreTenantStore<TenantStoreDbContext, TenantEntity, TenantMembershipEntity, TenantDataStoreEntity>(configure);
    }

    /// <summary>
    /// Registers the EF tenant store with application-defined entities, the way <c>IdentityDbContext&lt;TUser, TRole, ...&gt;</c>
    /// is extended. <typeparamref name="TContext"/> derives from <see cref="TenantStoreDbContext{TTenant,TMembership,TDataStore}"/>
    /// and adds whatever columns the application needs on its entity classes.
    /// </summary>
    /// <typeparam name="TContext">The application's tenant store context.</typeparam>
    /// <typeparam name="TTenant">The tenant entity (a company).</typeparam>
    /// <typeparam name="TMembership">The membership entity.</typeparam>
    /// <typeparam name="TDataStore">The tenant data-store entity.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the provider (for example <c>o =&gt; o.UseNpgsql(cs)</c>).</param>
    public static IServiceCollection AddEfCoreTenantStore<TContext, TTenant, TMembership, TDataStore>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configure)
        where TContext : TenantStoreDbContext<TTenant, TMembership, TDataStore>
        where TTenant : TenantEntity, new()
        where TMembership : TenantMembershipEntity, new()
        where TDataStore : TenantDataStoreEntity, new()
    {
        services.AddDbContext<TContext>(configure);

        // The generic services take the context under its base type, so a derived context satisfies them.
        services.TryAddScoped<TenantStoreDbContextBase>(sp => sp.GetRequiredService<TContext>());
        services.TryAddScoped<TenantStoreDbContext<TTenant, TMembership, TDataStore>>(sp => sp.GetRequiredService<TContext>());
        services.TryAddScoped<TenantManager<TTenant, TMembership, TDataStore>>();
        services.AddScoped<ITenantMemberProvisioner>(sp => sp.GetRequiredService<TenantManager<TTenant, TMembership, TDataStore>>());

        // The tenant store is a SINGLETON bridge that resolves the scoped EF store from a fresh scope per lookup: the singleton
        // tenant resolvers capture ITenantStore from the root provider, so a scoped registration would either throw at startup
        // (ValidateScopes) or share one DbContext across requests in production.
        services.RemoveAll<ITenantStore>();
        services.TryAddScoped<IEfTenantStore, EfTenantStore<TTenant, TMembership, TDataStore>>();
        services.AddSingleton<ITenantStore>(
            sp => new ScopedTenantStoreBridge(sp.GetRequiredService<IServiceScopeFactory>()));

        // Memberships live in the same context (one login across companies).
        services.RemoveAll<ITenantMembershipStore>();
        services.TryAddScoped<IEfTenantMembershipStore, EfTenantMembershipStore<TTenant, TMembership, TDataStore>>();
        services.AddSingleton<ITenantMembershipStore>(
            sp => new ScopedTenantMembershipStoreBridge(sp.GetRequiredService<IServiceScopeFactory>()));

        // The data-store catalog and the provisioner live in the same context: pointers and writes, never a secret value.
        services.TryAddScoped<ITenantDataStoreCatalog, EfTenantDataStoreCatalog<TTenant, TMembership, TDataStore>>();
        services.TryAddScoped<ITenantDataStoreProvisioner, TenantDataStoreProvisioner<TTenant, TMembership, TDataStore>>();
        return services;
    }

    /// <summary>
    /// Brings the default tenant-store schema up to date on startup. Runs in a fresh scope through the context's execution strategy
    /// so connection resilience applies.
    /// </summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="ensureCreatedIfNoMigrations">
    /// When <see langword="true"/> and no migrations are compiled into the context, falls back to <c>EnsureCreatedAsync</c>
    /// (convenient in Development). Leave <see langword="false"/> in production so a missing migration fails loudly.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public static Task MigrateTenantStoreAsync(
        this IServiceProvider services,
        bool ensureCreatedIfNoMigrations = false,
        CancellationToken ct = default)
        => services.MigrateTenantStoreAsync<TenantStoreDbContext>(ensureCreatedIfNoMigrations, ct);

    /// <summary>Brings the schema of an application's tenant store context up to date. See the default overload.</summary>
    /// <typeparam name="TContext">The tenant store context.</typeparam>
    /// <param name="services">The application service provider.</param>
    /// <param name="ensureCreatedIfNoMigrations">See the default overload.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task MigrateTenantStoreAsync<TContext>(
        this IServiceProvider services,
        bool ensureCreatedIfNoMigrations = false,
        CancellationToken ct = default)
        where TContext : DbContext
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            if (db.Database.GetMigrations().Any())
                await db.Database.MigrateAsync(ct);
            else if (ensureCreatedIfNoMigrations)
                await db.Database.EnsureCreatedAsync(ct);
            else
                throw NoMigrationsException(db);
        });
    }

    // Without migrations, the production path refuses to start: the schema would never be created and the first read would fail far from here.
    private static InvalidOperationException NoMigrationsException(DbContext db)
        => new(
            $"No migrations are compiled into {db.GetType().Name}, so its schema cannot be created outside Development. "
            + "Author them in the application (a context derived from it, with migrations) and apply them before the host starts.");
}
