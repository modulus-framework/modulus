using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;

namespace Modulus.Authorization.Extensions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;

public static class AuthorizationExtensions
{
    /// <summary>
    /// Registers the permission registry, the dynamic <c>:</c>-policy provider,
    /// and a hosted service that materialises all module permission declarations
    /// at startup.
    /// </summary>
    public static IServiceCollection AddModulusAuthorization(
        this IServiceCollection services)
    {
        services.AddSingleton<IPermissionRegistry, PermissionRegistry>();
        services.AddSingleton<IAuthorizationPolicyProvider,
            ModulusPermissionPolicyProvider>();
        services.AddAuthorizationCore();
        services.AddHostedService<PermissionInitHostedService>();
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
