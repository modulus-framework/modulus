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

        services.TryAddScoped<CurrentTenant>();
        services.TryAddScoped<ICurrentTenant>(
            sp => sp.GetRequiredService<CurrentTenant>());

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

    public MultiTenancyBuilder UseSubdomainResolver(
        string baseDomain)
    {
        services.AddSingleton<ITenantResolver>(
            sp => new Resolvers.SubdomainTenantResolver(
                sp.GetRequiredService<ITenantStore>(), baseDomain));
        return this;
    }
}
