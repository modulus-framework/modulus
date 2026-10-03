using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;

namespace Modulus.Security;

/// <summary>Registration of the unified <see cref="ISecurityContext"/>.</summary>
public static class SecurityContextExtensions
{
    /// <summary>
    /// Replaces the default <see cref="ISecurityContext"/> with <see cref="SecurityContext"/>
    /// (company, group, selected branch, correlation). Pair with
    /// <see cref="UseModulusSecurityContext"/> after authentication and <c>UseMultiTenancy()</c>.
    /// </summary>
    public static IServiceCollection AddModulusSecurityContext(
        this IServiceCollection services,
        Action<SecurityContextOptions>? configure = null)
    {
        services.AddHttpContextAccessor();
        services.AddOptions<SecurityContextOptions>();
        if (configure is not null)
            services.Configure(configure);

        services.TryAddScoped<CurrentBranch>();
        services.RemoveAll<ISecurityContext>();
        services.AddScoped<ISecurityContext, SecurityContext>();
        return services;
    }

    /// <summary>Adds <see cref="BranchContextMiddleware"/> (branch selection, checked against the org scope).</summary>
    public static IApplicationBuilder UseModulusSecurityContext(this IApplicationBuilder app)
        => app.UseMiddleware<BranchContextMiddleware>();
}
