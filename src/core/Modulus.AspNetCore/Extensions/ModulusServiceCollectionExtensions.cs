using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Modulus.AspNetCore.Extensions;


using Modulus.AspNetCore.Middleware;
using Modulus.Core;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;

public static class ModulusServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Modulus framework core: module loader + safe null
    /// defaults for <see cref="ICurrentUser"/> / <see cref="IPermissionRegistry"/>.
    /// Real identity/authorization modules override these with TryAdd semantics.
    /// </summary>
    public static IServiceCollection AddModulus(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ModulusBuilder> configure)
    {
        services.AddSingleton<IModuleLoader, ModuleLoader>();

        // Safe defaults — overridden when Identity / Authorization modules register.
        services.TryAddSingleton<IPermissionRegistry, NullPermissionRegistry>();
        services.TryAddScoped<ICurrentUser, NullCurrentUser>();

        var builder = new ModulusBuilder(services, configuration);
        configure(builder);
        return services;
    }

    /// <summary>
    /// Convenience overload that resolves <see cref="IConfiguration"/> from the
    /// collection. Prefer the overload that takes an <see cref="IConfiguration"/>
    /// argument to avoid building a transient service provider.
    /// </summary>
    public static IServiceCollection AddModulus(
        this IServiceCollection services,
        Action<ModulusBuilder> configure)
        => services.AddModulus(
            services.BuildServiceProvider(validateScopes: false)
                    .GetRequiredService<IConfiguration>(),
            configure);

    /// <summary>
    /// Registers the global RFC 7807 exception handler that maps
    /// Modulus exceptions to HTTP status codes.
    /// </summary>
    public static IServiceCollection AddModulusExceptionHandling(
        this IServiceCollection services)
    {
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        return services;
    }
}
