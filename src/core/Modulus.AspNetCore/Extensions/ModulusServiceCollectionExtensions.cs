using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Modulus.AspNetCore.Extensions;

using FluentValidation;
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

        // Safe defaults — overridden when Identity / Authorization / MultiTenancy
        // modules register.  TryAdd so the first (real) registration wins.
        services.TryAddSingleton<IPermissionRegistry, NullPermissionRegistry>();
        services.TryAddScoped<ICurrentUser, NullCurrentUser>();
        services.TryAddScoped<ICurrentTenant, NullCurrentTenant>();

        // Module lifecycle hosted service — initialised before the server
        // starts accepting connections (IHostedLifecycleService.StartingAsync).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IHostedService, ModuleLifecycleHostedService>());

        var builder = new ModulusBuilder(services, configuration);
        configure(builder);
        return services;
    }

    /// <summary>
    /// ABP-style convenience overload: auto-discovers the full module graph
    /// from <typeparamref name="TStartupModule"/> via <see cref="DependsOnAttribute"/>.
    /// </summary>
    /// <typeparam name="TStartupModule">The application's root module.</typeparam>
    /// <example>
    /// <code>
    /// builder.Services.AddModulus&lt;MyAppModule&gt;(builder.Configuration);
    /// </code>
    /// </example>
    public static IServiceCollection AddModulus<TStartupModule>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TStartupModule : class, IModule, new()
    {
        services.AddSingleton<IModuleLoader, ModuleLoader>();
        services.TryAddSingleton<IPermissionRegistry, NullPermissionRegistry>();
        services.TryAddScoped<ICurrentUser, NullCurrentUser>();
        services.TryAddScoped<ICurrentTenant, NullCurrentTenant>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IHostedService, ModuleLifecycleHostedService>());

        var builder = new ModulusBuilder(services, configuration);
        builder.AddModules<TStartupModule>();
        return services;
    }

    /// <summary>
    /// ABP-style convenience overload with additional manual configuration.
    /// </summary>
    public static IServiceCollection AddModulus<TStartupModule>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ModulusBuilder> configure)
        where TStartupModule : class, IModule, new()
    {
        services.AddSingleton<IModuleLoader, ModuleLoader>();
        services.TryAddSingleton<IPermissionRegistry, NullPermissionRegistry>();
        services.TryAddScoped<ICurrentUser, NullCurrentUser>();
        services.TryAddScoped<ICurrentTenant, NullCurrentTenant>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IHostedService, ModuleLifecycleHostedService>());

        var builder = new ModulusBuilder(services, configuration);
        builder.AddModules<TStartupModule>();
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
    {
        // Resolve IConfiguration without building a throwaway root provider —
        // ServiceCollectionContainerBuilderExtensions has a dedicated path for
        // this that avoids the singleton-disposal leak of BuildServiceProvider.
        var configuration = services
            .BuildServiceProvider(validateScopes: false)
            .GetRequiredService<IConfiguration>();
        return services.AddModulus(configuration, configure);
    }

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

    /// <summary>
    /// Registers REPR endpoint infrastructure: FluentValidation auto-discovery
    /// and API versioning.  Call from Program.cs:
    /// <code>
    /// builder.Services.AddModulusEndpoints(typeof(Program).Assembly);
    /// </code>
    /// </summary>
    public static IServiceCollection AddModulusEndpoints(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        // Auto-register all FluentValidation validators
        if (assemblies.Length > 0)
            services.AddValidatorsFromAssemblies(assemblies);

        return services;
    }
}
