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
        services.TryAddScoped<ICurrentDataScope, NullCurrentDataScope>();

        // TimeProvider — allows tests (and libraries) to freeze the clock via
        // a single seam instead of shimming DateTime.UtcNow at every callsite.
        services.TryAddSingleton(TimeProvider.System);

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
    private static void FinalizeModules(
        IServiceCollection services, ModulusBuilder builder)
        => builder.Complete();

    /// <summary>
    /// Composition root: registers the framework's null defaults, the module
    /// lifecycle hosted service, and every module registered through the
    /// <paramref name="configure"/> callback. Registration order is
    /// authoritative — it becomes the order of every module lifecycle phase
    /// (initialization runs in registration order, shutdown in reverse).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="configure">Registers modules via <see cref="ModulusBuilder.AddModule{TModule}"/>.</param>
    /// <example>
    /// <code>
    /// builder.Services.AddModulus(builder.Configuration, modules => modules
    ///     .AddModule&lt;DataModule&gt;()
    ///     .AddModule&lt;CatalogModule&gt;());
    /// </code>
    /// </example>
    public static IServiceCollection AddModulus(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ModulusBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        RegisterCoreDefaults(services);

        var builder = new ModulusBuilder(services, configuration);
        configure(builder);
        FinalizeModules(services, builder);
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
