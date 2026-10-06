using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;

namespace Modulus.MultiTenancy.Extensions;

public static class MultiTenancyExtensions
{
    public static IServiceCollection AddMultiTenancy(
        this IServiceCollection services,
        Action<MultiTenancyBuilder>? configure = null)
    {
        // Deny-by-default tenant store: resolvers can always resolve an
        // ITenantStore even before a real one is registered.
        services.TryAddSingleton<ITenantStore, NullTenantStore>();

        // Singleton, not scoped: CurrentTenant is a stateless accessor over a
        // static AsyncLocal — it carries no per-scope state, so one instance
        // serves every scope and async flow. Scoped registration would break
        // every singleton that reads the ambient tenant (cache tag keys, job
        // queues, bus ambient restores) under scope validation, for no
        // benefit: the AsyncLocal already isolates concurrent flows.
        services.TryAddSingleton<CurrentTenant>();
        UseAmbientCurrentTenant(services);

        // Empty until seeded: with RequireMembership() on and no real store, nobody without a
        // tenant claim can enter a tenant (fail-closed). AddEfCoreTenantStore replaces it.
        services.TryAddSingleton<InMemoryTenantMembershipStore>();
        services.TryAddSingleton<ITenantMembershipStore>(
            sp => sp.GetRequiredService<InMemoryTenantMembershipStore>());

        // Messages and jobs restore their tenant through the store: an unknown or deactivated
        // tenant id is rejected instead of becoming a data scope. Replaces the unverified default.
        services.RemoveAll<ITenantContextRestorer>();
        services.AddSingleton<ITenantContextRestorer, VerifiedTenantContextRestorer>();

        var builder = new MultiTenancyBuilder(services);
        configure?.Invoke(builder);
        return services;
    }

    /// <summary>
    /// Makes <see cref="CurrentTenant"/> the <see cref="ICurrentTenant"/> unless the app registered its own. <c>AddModulus</c>,
    /// <c>AddModulusUi</c> and the EF authorization store <c>TryAdd</c> <see cref="NullCurrentTenant"/> (always the host), and one
    /// registered first made a plain <c>TryAdd</c> here a no-op: every query saw every company and nothing was stamped. Only
    /// that default is replaced; a custom implementation registered earlier is kept.
    /// </summary>
    private static void UseAmbientCurrentTenant(IServiceCollection services)
    {
        var registered = services.Where(d => d.ServiceType == typeof(ICurrentTenant)).ToList();
        if (registered.Count > 0 && !registered.TrueForAll(IsNullDefault))
            return;

        services.RemoveAll<ICurrentTenant>();
        services.AddSingleton<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenant>());
    }

    private static bool IsNullDefault(ServiceDescriptor descriptor)
        => !descriptor.IsKeyedService
            && (descriptor.ImplementationType == typeof(NullCurrentTenant)
                || descriptor.ImplementationInstance is NullCurrentTenant);
}

public sealed class MultiTenancyBuilder(IServiceCollection services)
{
    public MultiTenancyBuilder UseHeaderResolver(
        string headerName = "X-Tenant-Id")
    {
        // Resolvers are stateless (they read the ambient request) and are injected
        // into TenantMiddleware, whose constructor is resolved from the ROOT
        // provider when the pipeline is built. They must therefore be singletons;
        // a scoped registration breaks startup when ValidateScopes is enabled.
        services.AddSingleton<ITenantResolver>(
            sp => new Resolvers.HeaderTenantResolver(
                sp.GetRequiredService<ITenantStore>(), headerName));
        return this;
    }

    public MultiTenancyBuilder UseJwtClaimResolver(
        string claimType = "tid")
    {
        services.AddSingleton<ITenantResolver>(
            sp => new Resolvers.JwtClaimTenantResolver(
                sp.GetRequiredService<ITenantStore>(), claimType));
        return this;
    }

    /// <summary>
    /// Requires an authenticated account without a tenant claim (a host-level account) to satisfy
    /// <paramref name="policy"/> before it may select a tenant through another resolver (for example
    /// the <c>X-Tenant-Id</c> header); otherwise the request gets 403. Without this call such an
    /// account may act in any tenant. Accounts that carry a tenant claim are always held to it, and
    /// anonymous requests are unaffected. The policy is evaluated with <c>IAuthorizationService</c>,
    /// so a Modulus permission name (<c>tenancy:switch</c>) works once <c>AddModulusAuthorization</c>
    /// is registered.
    /// </summary>
    public MultiTenancyBuilder RequireHostTenantAccessPolicy(string policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policy);
        services.Configure<TenantAccessOptions>(o => o.HostTenantAccessPolicy = policy);
        return this;
    }

    /// <summary>
    /// Company = tenant, one login across companies: an authenticated account without a tenant
    /// claim may enter the tenant a request selects (header, subdomain) only when it holds an
    /// active membership in it (<see cref="ITenantMembershipStore"/>); otherwise the request gets
    /// 403. A tenant claim (<c>tid</c>) still pins the token to its own tenant, and anonymous
    /// requests are unaffected. The policy set with <see cref="RequireHostTenantAccessPolicy"/>
    /// becomes the break-glass override for a non-member (logged as a warning). The membership is
    /// read on every request, so revoking it takes effect immediately. This is the default; the call
    /// remains so intent is explicit, and <see cref="AllowUnrestrictedTenantSelection"/> opts out.
    /// </summary>
    public MultiTenancyBuilder RequireMembership()
    {
        services.Configure<TenantAccessOptions>(o => o.RequireMembership = true);
        return this;
    }

    /// <summary>
    /// Lets an authenticated account without a tenant claim enter <b>any</b> tenant a request selects (header, subdomain),
    /// with no membership check. That is the single-operator model (one administrator running every company) and also the
    /// shape of a cross-tenant breach: any signed-in account of one company can read and write another's data by sending
    /// <c>X-Tenant-Id</c>. Membership is therefore the default; call this only when every such account is a trusted
    /// platform operator, and pair it with <see cref="RequireHostTenantAccessPolicy"/>.
    /// </summary>
    public MultiTenancyBuilder AllowUnrestrictedTenantSelection()
    {
        services.Configure<TenantAccessOptions>(o => o.RequireMembership = false);
        return this;
    }

    public MultiTenancyBuilder UseSubdomainResolver(
        string baseDomain)
    {
        services.AddSingleton<ITenantResolver>(
            sp => new Resolvers.SubdomainTenantResolver(
                sp.GetRequiredService<ITenantStore>(), baseDomain));
        return this;
    }
}

internal sealed class TenantAccessOptions
{
    public string? HostTenantAccessPolicy { get; set; }

    public bool RequireMembership { get; set; } = true;
}
