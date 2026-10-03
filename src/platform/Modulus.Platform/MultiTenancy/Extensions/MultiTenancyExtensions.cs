using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;

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
        services.TryAddSingleton<ICurrentTenant>(
            sp => sp.GetRequiredService<CurrentTenant>());

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
    /// read on every request, so revoking it takes effect immediately.
    /// </summary>
    public MultiTenancyBuilder RequireMembership()
    {
        services.Configure<TenantAccessOptions>(o => o.RequireMembership = true);
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

    public bool RequireMembership { get; set; }
}
