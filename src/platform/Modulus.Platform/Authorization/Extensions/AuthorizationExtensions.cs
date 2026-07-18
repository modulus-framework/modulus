using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Authorization.Features;
using Modulus.Authorization.Fields;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Organization;
using Modulus.Authorization.Resources;
using Modulus.Core.Abstractions;

namespace Modulus.Authorization.Extensions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;

public static class AuthorizationExtensions
{
    /// <summary>
    /// Registers the permission registry, the dynamic <c>:</c>-policy provider,
    /// a hosted service that materialises all module permission declarations at
    /// startup, and the server-side grant store + effective-permission resolver
    /// (empty and therefore fail-closed until grants are seeded via
    /// <see cref="AddPermissionGrants"/>).
    /// </summary>
    public static IServiceCollection AddModulusAuthorization(
        this IServiceCollection services)
    {
        services.AddSingleton<IPermissionRegistry, PermissionRegistry>();
        services.AddSingleton<IAuthorizationPolicyProvider,
            ModulusPermissionPolicyProvider>();
        services.AddAuthorizationCore();
        services.AddHostedService<PermissionInitHostedService>();

        // Grant store + resolver. TryAdd so a later increment (e.g. an EF-backed
        // store) can supersede the in-memory default by registering first.
        services.TryAddSingleton<IPermissionGrantStore>(sp =>
        {
            var store = new InMemoryPermissionGrantStore();
            foreach (var seed in sp.GetServices<IPermissionGrantSeed>())
                seed.Apply(store);
            return store;
        });
        // Register the concrete resolver once and map the interface to it, so the
        // delegation-aware decorator (AddDelegation) and the effective-access reporter can
        // depend on the *direct* resolver (bypassing delegation) without a second instance.
        services.TryAddSingleton<PermissionResolver>();
        services.TryAddSingleton<IPermissionResolver>(sp => sp.GetRequiredService<PermissionResolver>());

        // Organizational scope: hierarchy + placements + scope resolver. TryAdd so
        // an EF-backed store can supersede the in-memory defaults by registering
        // first. Empty until seeded via AddOrganization — fail-closed.
        services.TryAddSingleton<IOrgHierarchy>(sp =>
        {
            var hierarchy = new InMemoryOrgHierarchy();
            foreach (var seed in sp.GetServices<IOrgHierarchySeed>())
                seed.Apply(hierarchy);
            return hierarchy;
        });
        services.TryAddSingleton<IOrgPlacementStore>(sp =>
        {
            var store = new InMemoryOrgPlacementStore();
            foreach (var seed in sp.GetServices<IOrgPlacementSeed>())
                seed.Apply(store);
            return store;
        });
        services.TryAddSingleton<IOrgScopeResolver, OrgScopeResolver>();

        // Feature entitlements: catalog + per-tenant entitlement store + resolver. TryAdd
        // so an EF-backed store can supersede the in-memory defaults by registering first.
        // Empty until seeded via AddFeatureEntitlements — fail-closed once AddFeatureGate
        // wires the gate. The catalog is assembled from module AddFeatures declarations.
        services.TryAddSingleton<IFeatureCatalog, FeatureCatalog>();
        services.TryAddSingleton<IFeatureEntitlementStore>(sp =>
        {
            var store = new InMemoryFeatureEntitlementStore();
            foreach (var seed in sp.GetServices<IFeatureEntitlementSeed>())
                seed.Apply(store);
            return store;
        });
        services.TryAddSingleton<IFeatureEntitlementResolver, FeatureEntitlementResolver>();

        // Governance defaults so the effective-access reporter is always resolvable:
        // no delegations and no SoD constraints until AddDelegation / AddSegregationOfDuties
        // opt in. TimeProvider backs decision-time delegation-window checks.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDelegationResolver>(EmptyDelegationResolver.Instance);
        services.TryAddSingleton<ISodPolicy>(SodPolicy.Empty);
        services.TryAddSingleton<IEffectiveAccessService, EffectiveAccessService>();
        return services;
    }

    /// <summary>
    /// Seeds authorization grants (role→permission and user→permission allows and
    /// denies) into the in-memory grant store. Multiple calls accumulate; all seeds
    /// are applied when the store is first resolved. Grants are dynamic — they may
    /// also be mutated at runtime through <see cref="InMemoryPermissionGrantStore"/>.
    /// </summary>
    public static IServiceCollection AddPermissionGrants(
        this IServiceCollection services,
        Action<InMemoryPermissionGrantStore> seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        services.AddSingleton<IPermissionGrantSeed>(new PermissionGrantSeed(seed));
        return services;
    }

    /// <summary>
    /// Seeds the organizational model: the unit hierarchy (a tree or matrixed DAG)
    /// and users' placements within it. Multiple calls accumulate; all seeds are
    /// applied when the singletons are first resolved. Both the hierarchy and the
    /// placements are dynamic — they may also be mutated at runtime through
    /// <see cref="InMemoryOrgHierarchy"/> / <see cref="InMemoryOrgPlacementStore"/>
    /// (reorganizations and reassignments are supported operations).
    /// </summary>
    public static IServiceCollection AddOrganization(
        this IServiceCollection services,
        Action<InMemoryOrgHierarchy>? hierarchy = null,
        Action<InMemoryOrgPlacementStore>? placements = null)
    {
        if (hierarchy is not null)
            services.AddSingleton<IOrgHierarchySeed>(new OrgHierarchySeed(hierarchy));
        if (placements is not null)
            services.AddSingleton<IOrgPlacementSeed>(new OrgPlacementSeed(placements));
        return services;
    }

    /// <summary>
    /// Turns on organizational <b>row-scoping</b> for entities marked
    /// <see cref="Modulus.Core.Abstractions.Entities.IHasOrgUnit"/>: registers the
    /// scoped <see cref="ICurrentDataScope"/> bridge that resolves the current
    /// principal's org scope (identity → placements → traversal closure) for the
    /// <c>ModuleDbContext</c> query filter to read. Opt-in and fail-closed — until
    /// called, the framework's <see cref="Modulus.Core.Null.NullCurrentDataScope"/>
    /// leaves the org filter a no-op; once called, an unauthenticated or unplaced
    /// principal sees no <see cref="Modulus.Core.Abstractions.Entities.IHasOrgUnit"/>
    /// rows. Requires the organizational model to be seeded via
    /// <see cref="AddOrganization"/> (and a real <see cref="ICurrentUser"/>, i.e. the
    /// Identity module). Registered with <c>TryAddScoped</c> so a custom scope can
    /// supersede it.
    /// </summary>
    public static IServiceCollection AddOrgDataScope(this IServiceCollection services)
    {
        services.TryAddScoped<ICurrentDataScope, CurrentDataScope>();
        return services;
    }

    /// <summary>
    /// Registers a declarative resource/workflow <see cref="ResourcePolicy"/> for
    /// resource type <typeparamref name="T"/> and, on first use, the scoped
    /// <see cref="IResourceAuthorizer"/> that enforces it — the instance-level
    /// authorization layer (blueprint §5.7, §5.8). Call
    /// <c>authorizer.Authorize(record, "approve")</c> in a handler after loading the
    /// record. Fail-closed: a type with no policy, or an action no rule grants,
    /// denies. Multiple calls register policies for different types (last one for a
    /// given type wins). Requires a real <see cref="ICurrentUser"/> (Identity module)
    /// and — for scope-conditioned rules — <see cref="AddOrgDataScope"/>.
    /// </summary>
    public static IServiceCollection AddResourcePolicy<T>(
        this IServiceCollection services, ResourcePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        services.AddSingleton(new ResourcePolicyRegistration(typeof(T), policy));
        services.TryAddSingleton<IResourcePolicyRegistry, ResourcePolicyRegistry>();
        services.TryAddScoped<IResourceAuthorizer, ResourceAuthorizer>();
        return services;
    }

    /// <summary>
    /// Registers a declarative <see cref="FieldSecurityProfile"/> for resource type
    /// <typeparamref name="T"/> and, on first use, the scoped
    /// <see cref="IFieldAuthorizer"/> that enforces it — the field-level security layer
    /// (blueprint §5.9, §11). Use <c>authorizer.Redact(dto)</c> at the read/projection
    /// boundary to mask fields the caller may not see, and
    /// <c>authorizer.AuthorizeWrite(typeof(T), attemptedFields)</c> at the command
    /// boundary to reject writes to fields above their clearance. Fail-closed: a field
    /// classified on the model is protected by the built-in deny-by-default rules even
    /// before a profile opens it. Multiple calls register profiles for different types
    /// (last one for a given type wins). Requires a real <see cref="ICurrentUser"/>
    /// (Identity module).
    /// </summary>
    public static IServiceCollection AddFieldSecurity<T>(
        this IServiceCollection services, FieldSecurityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        services.AddSingleton(new FieldSecurityRegistration(typeof(T), profile));
        services.TryAddSingleton<IFieldSecurityRegistry, FieldSecurityRegistry>();
        services.TryAddScoped<IFieldAuthorizer, FieldAuthorizer>();
        return services;
    }

    /// <summary>
    /// Declares module <b>feature catalog</b> entries — the capabilities whose
    /// <i>availability</i> is governed by entitlement (licensing / plan / jurisdiction),
    /// parallel to <see cref="AddPermissions"/> (blueprint §5.11, §14). Declarations
    /// accumulate across modules and are assembled into the <see cref="IFeatureCatalog"/>
    /// for administrative discovery; enforcement is driven by the entitlement store, so a
    /// feature no plan grants is off regardless of whether it is catalogued.
    /// </summary>
    public static IServiceCollection AddFeatures(
        this IServiceCollection services, params FeatureDefinition[] features)
    {
        ArgumentNullException.ThrowIfNull(features);
        foreach (var feature in features)
            services.AddSingleton(new FeatureCatalogRegistration(feature));
        return services;
    }

    /// <summary>
    /// Seeds the per-tenant entitlement model: plans (named feature bundles), the plan
    /// each tenant is on, and per-tenant overrides. Multiple calls accumulate; all seeds
    /// are applied when the store is first resolved. The store is dynamic — a billing or
    /// admin flow may also mutate it at runtime through
    /// <see cref="InMemoryFeatureEntitlementStore"/> (an upgrade, add-on purchase, or
    /// suspension takes effect immediately).
    /// </summary>
    public static IServiceCollection AddFeatureEntitlements(
        this IServiceCollection services,
        Action<InMemoryFeatureEntitlementStore> seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        services.AddSingleton<IFeatureEntitlementSeed>(new FeatureEntitlementSeed(seed));
        return services;
    }

    /// <summary>
    /// Turns on <b>feature gating</b>: registers the scoped <see cref="IFeatureGate"/>
    /// bridge that resolves the current tenant's entitlements
    /// (<see cref="ICurrentTenant"/> → plan + overrides) for <c>[RequireFeature]</c> and
    /// any <see cref="IFeatureGate"/> check to read — the layer that sits <i>above and
    /// before</i> per-user permissions (blueprint §5.11, §14). Opt-in and fail-closed:
    /// until called, the framework's <see cref="Modulus.Core.Null.NullFeatureGate"/>
    /// leaves every feature enabled; once called, a feature no plan or override grants —
    /// or a request with no tenant resolved — is denied. Seed the model via
    /// <see cref="AddFeatureEntitlements"/>. Registered with <c>TryAddScoped</c> so a
    /// custom gate can supersede it.
    /// </summary>
    public static IServiceCollection AddFeatureGate(this IServiceCollection services)
    {
        services.TryAddScoped<IFeatureGate, FeatureGate>();
        return services;
    }

    /// <summary>
    /// Turns on <b>delegation and temporary access</b> (blueprint §5.13, §15): registers
    /// the runtime-mutable <see cref="InMemoryDelegationStore"/> and the
    /// <see cref="IDelegationResolver"/>, and — crucially — decorates
    /// <see cref="IPermissionResolver"/> with
    /// <see cref="DelegationAwarePermissionResolver"/> so authority delegated to a
    /// principal takes effect at <see cref="ICurrentUser.HasPermission"/> with no change
    /// to the permission checker. Delegations are <b>time-bounded and enforced at decision
    /// time</b>, <b>revocable</b> immediately, and <b>capped by the delegator's own direct
    /// authority</b> (which also bounds sub-delegation). Opt-in; seed baseline delegations
    /// via the optional <paramref name="seed"/>. Requires <see cref="AddModulusAuthorization"/>.
    /// </summary>
    public static IServiceCollection AddDelegation(
        this IServiceCollection services,
        Action<InMemoryDelegationStore>? seed = null)
    {
        if (seed is not null)
            services.AddSingleton<IDelegationSeed>(new DelegationSeed(seed));

        services.TryAddSingleton<IDelegationStore>(sp =>
        {
            var store = new InMemoryDelegationStore();
            foreach (var s in sp.GetServices<IDelegationSeed>())
                s.Apply(store);
            return store;
        });

        // The resolver caps against the delegator's DIRECT authority (concrete resolver),
        // never the delegation-aware decorator — so delegated authority is not re-delegable.
        services.Replace(ServiceDescriptor.Singleton<IDelegationResolver>(sp =>
            new DelegationResolver(
                sp.GetRequiredService<IDelegationStore>(),
                sp.GetRequiredService<PermissionResolver>(),
                sp.GetRequiredService<TimeProvider>())));

        // Decorate the capability resolver so HasPermission includes delegated authority.
        services.Replace(ServiceDescriptor.Singleton<IPermissionResolver>(sp =>
            new DelegationAwarePermissionResolver(
                sp.GetRequiredService<PermissionResolver>(),
                sp.GetRequiredService<IDelegationResolver>())));
        return services;
    }

    /// <summary>
    /// Registers a <b>segregation-of-duties</b> policy — sets of mutually-exclusive
    /// permissions ("maker cannot be checker") evaluated as an analyzable standing control
    /// (blueprint §5.6, §13). The resulting <see cref="ISodPolicy"/> feeds the
    /// <see cref="IEffectiveAccessService"/> report (flagging users who hold a toxic
    /// combination) and can be called from a grant-admin flow to prevent creating one.
    /// Supersedes the empty default. Requires <see cref="AddModulusAuthorization"/>.
    /// </summary>
    public static IServiceCollection AddSegregationOfDuties(
        this IServiceCollection services, params SodConstraint[] constraints)
    {
        ArgumentNullException.ThrowIfNull(constraints);
        services.Replace(ServiceDescriptor.Singleton<ISodPolicy>(new SodPolicy(constraints)));
        return services;
    }

    /// <summary>
    /// Declares a module's permissions. Declarations are captured and replayed
    /// against the registry singleton at startup (no transient provider built).
    /// </summary>
    public static IServiceCollection AddPermissions(
        this IServiceCollection services,
        string moduleName,
        Action<IPermissionRegistry> configure)
    {
        services.AddSingleton<IPermissionRegistration>(
            new PermissionRegistration(moduleName, configure));
        return services;
    }
}
