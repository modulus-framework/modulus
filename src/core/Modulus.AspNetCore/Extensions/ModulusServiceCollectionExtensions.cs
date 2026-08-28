using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Modulus.AspNetCore.Extensions;

using Modulus.AspNetCore.Middleware;
using Modulus.Core;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;

public static class ModulusServiceCollectionExtensions
{
    /// <summary>
    /// Registers the framework's null defaults and the module-lifecycle hosted
    /// service. Idempotent (TryAdd everywhere) so the overloads can share it.
    /// </summary>
    private static void RegisterCoreDefaults(IServiceCollection services)
    {
        // Safe defaults — overridden when Identity / Authorization / MultiTenancy
        // modules register.  TryAdd so the first (real) registration wins.
        services.TryAddSingleton<IPermissionRegistry, NullPermissionRegistry>();
        services.TryAddScoped<ICurrentUser, NullCurrentUser>();
        services.TryAddScoped<ICurrentTenant, NullCurrentTenant>();

        // Module lifecycle hosted service — initialised before the server
        // starts accepting connections (IHostedLifecycleService.StartingAsync).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IHostedService, ModuleLifecycleHostedService>());
    }

    /// <summary>
    /// Builds the module dependency graph <b>eagerly</b> so an app that forgets
    /// to call <c>UseModulus()</c> still initializes its modules — the previous
    /// design left <see cref="IModuleLoader"/> empty until <c>UseModulus()</c>
    /// ran, so a missing call silently skipped every module's
    /// <c>InitializeAsync</c>.
    /// </summary>
    private static void FinalizeModuleGraph(
        IServiceCollection services, ModulusBuilder builder)
        => builder.Complete();

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
        RegisterCoreDefaults(services);

        var builder = new ModulusBuilder(services, configuration);
        builder.AddModules<TStartupModule>();
        FinalizeModuleGraph(services, builder);
        return services;
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
}
