using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Authorization.Features;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Organization;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// Registration + initialisation for the EF Core-backed authorization stores.
/// </summary>
public static class EfAuthorizationStoreExtensions
{
    /// <summary>
    /// Replaces the in-memory defaults registered by
    /// <c>AddModulusAuthorization()</c> with EF Core-backed stores for
    /// permission grants, the organizational hierarchy and placements, feature
    /// entitlements, and delegations — turning the whole authorization stack
    /// into durable, runtime-editable data. Call order relative to
    /// <c>AddModulusAuthorization()</c> / <c>AddDelegation()</c> does not
    /// matter: the in-memory defaults use <c>TryAdd</c> and this method removes
    /// any existing registration first.
    /// <code>
    /// services.AddModulusAuthorization();
    /// services.AddEfCoreAuthorizationStores(o => o.UseNpgsql(connectionString));
    /// </code>
    /// The context is registered only through
    /// <see cref="IDbContextFactory{TContext}"/> — never as <see cref="DbContext"/>
    /// — so it stays out of the module transaction fan-out and the module
    /// migration loop; initialise its schema with
    /// <see cref="MigrateAuthorizationStoreAsync"/>.
    /// </summary>
    /// <remarks>
    /// Startup seeds targeted at the in-memory stores
    /// (<c>AddPermissionGrants</c>, <c>AddOrganization</c>,
    /// <c>AddFeatureEntitlements</c>, and the <c>AddDelegation</c> seed) do
    /// <b>not</b> apply to the EF stores — durable data is managed through the
    /// stores' async management methods (or your own provisioning/migration
    /// code), not re-seeded on every boot.
    /// </remarks>
    public static IServiceCollection AddEfCoreAuthorizationStores(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configure)
    {
        services.AddDbContextFactory<AuthorizationStoreDbContext>(configure);
        services.TryAddSingleton(TimeProvider.System);

        // Register each concrete store once and map its seam interface onto it,
        // so applications can inject the concrete type for the async management
        // API while the resolvers keep depending on the seam.
        services.TryAddSingleton<EfPermissionGrantStore>();
        services.RemoveAll<IPermissionGrantStore>();
        services.AddSingleton<IPermissionGrantStore>(
            sp => sp.GetRequiredService<EfPermissionGrantStore>());

        services.TryAddSingleton<EfOrgHierarchy>();
        services.RemoveAll<IOrgHierarchy>();
        services.AddSingleton<IOrgHierarchy>(
            sp => sp.GetRequiredService<EfOrgHierarchy>());

        services.TryAddSingleton<EfOrgPlacementStore>();
        services.RemoveAll<IOrgPlacementStore>();
        services.AddSingleton<IOrgPlacementStore>(
            sp => sp.GetRequiredService<EfOrgPlacementStore>());

        services.TryAddSingleton<EfFeatureEntitlementStore>();
        services.RemoveAll<IFeatureEntitlementStore>();
        services.AddSingleton<IFeatureEntitlementStore>(
            sp => sp.GetRequiredService<EfFeatureEntitlementStore>());

        services.TryAddSingleton<EfDelegationStore>();
        services.RemoveAll<IDelegationStore>();
        services.AddSingleton<IDelegationStore>(
            sp => sp.GetRequiredService<EfDelegationStore>());

        return services;
    }

    /// <summary>
    /// Brings the authorization-store schema up to date on startup. Runs in a
    /// fresh scope through the context's execution strategy so connection
    /// resilience applies.
    /// </summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="ensureCreatedIfNoMigrations">
    /// When <see langword="true"/> and no migrations are compiled into the
    /// context, falls back to <c>EnsureCreatedAsync</c> (convenient in
    /// Development). Leave <see langword="false"/> in production so a missing
    /// migration fails loudly.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task MigrateAuthorizationStoreAsync(
        this IServiceProvider services,
        bool ensureCreatedIfNoMigrations = false,
        CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var factory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            if (!ensureCreatedIfNoMigrations || db.Database.GetMigrations().Any())
                await db.Database.MigrateAsync(ct);
            else
                await db.Database.EnsureCreatedAsync(ct);
        });
    }
}
