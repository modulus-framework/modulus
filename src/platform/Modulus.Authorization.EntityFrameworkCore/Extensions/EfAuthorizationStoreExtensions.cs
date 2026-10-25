using Modulus.Authorization.Approval;
using Modulus.Authorization.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Authorization.Features;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Organization;
using Modulus.Authorization.Scopes;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;

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
        // The framework's own organization types: the generic registrations below resolve these default classes.
        services.TryAddSingleton<EfOrgHierarchy>();
        services.TryAddSingleton<EfOrgHierarchy<OrgUnitRow, OrgUnitParentRow, OrgUnitProfileRow, CompanyProfileRow>>(
            sp => sp.GetRequiredService<EfOrgHierarchy>());
        services.TryAddSingleton<EfOrganizationProfileStore>();
        services.TryAddSingleton<EfOrganizationProfileStore<OrgUnitRow, OrgUnitParentRow, OrgUnitProfileRow, CompanyProfileRow>>(
            sp => sp.GetRequiredService<EfOrganizationProfileStore>());
        return services.AddEfCoreAuthorizationStores<AuthorizationStoreDbContext, OrgUnitRow, OrgUnitParentRow, OrgUnitProfileRow, CompanyProfileRow>(configure);
    }

    /// <summary>
    /// Registers the EF authorization stores with application-defined organization entities, the way <c>IdentityDbContext</c> is
    /// extended. <typeparamref name="TContext"/> derives from <see cref="AuthorizationStoreDbContext{TOrgUnit,TOrgUnitParent,TOrgUnitProfile,TCompanyProfile}"/>.
    /// </summary>
    public static IServiceCollection AddEfCoreAuthorizationStores<TContext, TOrgUnit, TOrgUnitParent, TOrgUnitProfile, TCompanyProfile>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configure)
        where TContext : AuthorizationStoreDbContext<TOrgUnit, TOrgUnitParent, TOrgUnitProfile, TCompanyProfile>
        where TOrgUnit : OrgUnitRow, new()
        where TOrgUnitParent : OrgUnitParentRow, new()
        where TOrgUnitProfile : OrgUnitProfileRow, new()
        where TCompanyProfile : CompanyProfileRow, new()
    {
        services.AddDbContextFactory<TContext>(configure);

        // Stores that need only the base context, and stores that need the organization types, both resolve the application's
        // context through an adapter (IDbContextFactory<T> is invariant).
        services.TryAddSingleton<IDbContextFactory<AuthorizationStoreDbContextBase>>(
            sp => new ContextFactoryAdapter<AuthorizationStoreDbContextBase, TContext>(sp.GetRequiredService<IDbContextFactory<TContext>>()));
        services.TryAddSingleton<IDbContextFactory<AuthorizationStoreDbContext<TOrgUnit, TOrgUnitParent, TOrgUnitProfile, TCompanyProfile>>>(
            sp => new ContextFactoryAdapter<AuthorizationStoreDbContext<TOrgUnit, TOrgUnitParent, TOrgUnitProfile, TCompanyProfile>, TContext>(sp.GetRequiredService<IDbContextFactory<TContext>>()));
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<PermissionCatalogOptions>();

        // Fail-safe default (mirrors AddModulusAuthorizationManagement's
        // ICurrentUser guard below): a host that never touches multi-tenancy
        // keeps working exactly as before — NullCurrentTenant.IsHost is
        // always true, so the tenant query filter added for B4 bypasses
        // entirely and every row stays visible, unfiltered.
        services.TryAddScoped<ICurrentTenant, NullCurrentTenant>();

        // Register the concrete store for the management API, then expose the
        // seam interface as the request-scoped cache wrapper — the ONLY
        // IPermissionGrantStore registration, so every consumer (resolver
        // chain, authorization handler) is scoped and memoizes GetGrants per
        // principal within a single request, eliminating redundant DB queries
        // when multiple permissions are checked against the same principal.
        services.TryAddSingleton<EfPermissionGrantStore>();
        // Optional node-wide grant copy (Authorization:GrantCache:Duration, off by default). It is also told about every access change.
        services.AddOptions<PermissionGrantCacheOptions>();
        // The configured starting grants of a new company (CompanyProvisioning:Grants). Empty unless configured.
        services.AddOptions<CompanyProvisioningOptions>();
        services.TryAddScoped<ICompanyProvisioner, CompanyProvisioner>();
        // Explicit parent-to-child authorities (decision 6). Read directly on every check, never cached.
        services.TryAddSingleton<ICompanyAuthorityStore, EfCompanyAuthorityStore>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TtlPermissionGrantStore>(sp => new TtlPermissionGrantStore(
            sp.GetRequiredService<EfPermissionGrantStore>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptions<PermissionGrantCacheOptions>>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IAccessChangeObserver>(sp => sp.GetRequiredService<TtlPermissionGrantStore>());
        services.RemoveAll<IPermissionGrantStore>();
        services.AddScoped<IPermissionGrantStore>(sp =>
            new CachedPermissionGrantStore(sp.GetRequiredService<TtlPermissionGrantStore>(), sp.GetService<ICurrentTenant>()));

        services.TryAddSingleton<EfOrgHierarchy<TOrgUnit, TOrgUnitParent, TOrgUnitProfile, TCompanyProfile>>();
        services.TryAddSingleton<IOrgUnitAdministration>(sp => sp.GetRequiredService<EfOrgHierarchy<TOrgUnit, TOrgUnitParent, TOrgUnitProfile, TCompanyProfile>>());
        services.RemoveAll<IOrgHierarchy>();
        services.AddSingleton<IOrgHierarchy>(
            sp => sp.GetRequiredService<EfOrgHierarchy<TOrgUnit, TOrgUnitParent, TOrgUnitProfile, TCompanyProfile>>());

        services.TryAddSingleton<EfOrgPlacementStore>();
        services.RemoveAll<IOrgPlacementStore>();
        services.AddSingleton<IOrgPlacementStore>(
            sp => sp.GetRequiredService<EfOrgPlacementStore>());

        services.TryAddSingleton<EfFeatureEntitlementStore>();
        services.RemoveAll<IFeatureEntitlementStore>();
        services.AddSingleton<IFeatureEntitlementStore>(
            sp => sp.GetRequiredService<EfFeatureEntitlementStore>());

        services.TryAddSingleton<EfDelegationStore>();

        // Delegation caps follow the delegator's live roles: a timer plus an immediate refresh on access changes.
        services.AddOptions<DelegationRoleRefreshOptions>();
        services.TryAddSingleton(sp => new DelegationRoleRefresher(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<EfDelegationStore>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddHostedService<DelegationRoleRefreshService>();
        services.AddSingleton<IAccessChangeObserver, DelegationRoleRefreshObserver>();
        services.RemoveAll<IDelegationStore>();
        services.AddSingleton<IDelegationStore>(
            sp => sp.GetRequiredService<EfDelegationStore>());

        services.TryAddSingleton<EfAssignmentStore>();
        services.RemoveAll<IAssignmentStore>();
        services.AddSingleton<IAssignmentStore>(
            sp => sp.GetRequiredService<EfAssignmentStore>());

        services.TryAddSingleton<EfAccessRequestStore>();
        services.TryAddSingleton<EfPermissionExceptionStore>();
        services.TryAddSingleton<EfOrganizationProfileStore<TOrgUnit, TOrgUnitParent, TOrgUnitProfile, TCompanyProfile>>();
        services.TryAddSingleton<EfSodRuleStore>();
        services.TryAddSingleton<IPermissionCatalogSnapshotStore, EfPermissionCatalogSnapshotStore>();
        services.TryAddSingleton<EfRoleInclusionStore>();
        services.TryAddSingleton<EfPositionStore>();
        services.TryAddSingleton<EfPartyStore>();
        services.TryAddSingleton<Modulus.Authorization.Dimensions.IDimensionValueRegistry, EfDimensionValueRegistry>();
        // Findings are kept in the database (A4-1): the decision path only enqueues, and a hosted service writes and purges.
        services.AddOptions<DataQualityFindingOptions>();
        services.TryAddSingleton(sp => new DataQualityFindingQueue(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DataQualityFindingOptions>>().Value.Capacity));
        services.Replace(ServiceDescriptor.Singleton<Modulus.Core.Abstractions.DataQuality.IDataQualityFindingSink>(sp => sp.GetRequiredService<DataQualityFindingQueue>()));
        services.TryAddSingleton<DataQualityFindingStore>();
        services.AddHostedService<DataQualityFindingService>();
        // Replaces the framework's null hierarchy whatever the registration order: the placements are in the database.
        services.Replace(ServiceDescriptor.Singleton<Modulus.Authorization.Dimensions.IDimensionHierarchy, EfDimensionHierarchy>());
        // Rules edited at runtime plus the ones declared in code (AddSegregationOfDuties); scoped, read once per request.
        services.RemoveAll<ISodPolicy>();
        services.AddScoped<ISodPolicy, EfSodPolicy>();
        // Record shares (A8-1): durable, so a share outlives a restart.
        services.TryAddSingleton<EfRecordShareStore>();
        services.RemoveAll<Modulus.Authorization.Sharing.IRecordShareStore>();
        services.AddSingleton<Modulus.Authorization.Sharing.IRecordShareStore>(sp => sp.GetRequiredService<EfRecordShareStore>());

        services.TryAddSingleton<EfApprovalAuthorityStore>();
        services.RemoveAll<IApprovalAuthorityStore>();
        services.AddSingleton<IApprovalAuthorityStore>(
            sp => sp.GetRequiredService<EfApprovalAuthorityStore>());

        // Multi-step approval chains and requests (AddApprovalWorkflow registers the engine and falls back to memory).
        services.TryAddSingleton<EfApprovalChainStore>();
        services.RemoveAll<IApprovalChainStore>();
        services.AddSingleton<IApprovalChainStore>(sp => sp.GetRequiredService<EfApprovalChainStore>());
        services.TryAddSingleton<EfApprovalRequestStore>();
        services.RemoveAll<IApprovalRequestStore>();
        services.AddSingleton<IApprovalRequestStore>(sp => sp.GetRequiredService<EfApprovalRequestStore>());

        services.TryAddSingleton<EfRecertificationCampaignStore>();
        services.RemoveAll<IRecertificationCampaignStore>();
        services.AddSingleton<IRecertificationCampaignStore>(
            sp => sp.GetRequiredService<EfRecertificationCampaignStore>());

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
            .GetRequiredService<IDbContextFactory<AuthorizationStoreDbContextBase>>();
        await using var db = await factory.CreateDbContextAsync(ct);
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

    /// <summary>
    /// Writes the startup seeds (<c>AddPermissionGrants</c>) into the durable grant store, once per database: a marker records the
    /// seed, so a grant revoked later stays revoked, even when that revocation leaves the store empty. Only plain role and user allows
    /// and denies are seeded; a scoped or time-limited grant is refused, because the seed cannot store it durably. Call after
    /// <see cref="MigrateAuthorizationStoreAsync"/>.
    /// </summary>
    public static async Task SeedPermissionGrantsAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        var seeds = scope.ServiceProvider.GetServices<IPermissionGrantSeed>().ToList();
        if (seeds.Count == 0)
            return;

        var store = scope.ServiceProvider.GetRequiredService<EfPermissionGrantStore>();
        if (await store.IsSeedAppliedAsync(PermissionGrantSeedName, ct))
            return;

        var staging = new InMemoryPermissionGrantStore();
        foreach (var seed in seeds)
            seed.Apply(staging);

        foreach (var grant in staging.Snapshot())
        {
            if (grant.Scope is not null || grant.ValidFrom is not null || grant.ValidUntil is not null)
                throw new NotSupportedException(
                    $"The seeded grant {grant.Permission} for {grant.Holder} is scoped or time-limited; create it through the grant API instead.");

            var permissions = new[] { grant.Permission };
            if (grant.HolderType == GrantHolderType.Role)
            {
                await (grant.Type == PermissionGrantType.Allow
                    ? store.GrantToRoleAsync(grant.Holder, permissions, ct)
                    : store.DenyToRoleAsync(grant.Holder, permissions, ct));
            }
            else
            {
                var userId = Guid.Parse(grant.Holder);
                await (grant.Type == PermissionGrantType.Allow
                    ? store.GrantToUserAsync(userId, permissions, ct)
                    : store.DenyToUserAsync(userId, permissions, ct));
            }
        }

        var clock = scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        await store.MarkSeedAppliedAsync(PermissionGrantSeedName, clock.GetUtcNow(), ct);
    }

    private const string PermissionGrantSeedName = "permission-grants";

    // Without migrations, the production path refuses to start: the schema would never be created and the first read would fail far from here.
    private static InvalidOperationException NoMigrationsException(DbContext db)
        => new(
            $"No migrations are compiled into {db.GetType().Name}, so its schema cannot be created outside Development. "
            + "Author them in the application (a context derived from it, with migrations) and apply them before the host starts.");
}
