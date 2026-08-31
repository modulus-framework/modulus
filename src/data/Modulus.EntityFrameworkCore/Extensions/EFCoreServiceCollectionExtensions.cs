namespace Modulus.EntityFrameworkCore.Extensions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Data.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;

public static class EFCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers a module <typeparamref name="TContext"/> together with the
    /// generic <see cref="EfRepository{T}"/> / <see cref="EfReadRepository{T}"/>,
    /// and exposes the context as <see cref="DbContext"/> so that
    /// <c>TransactionBehavior</c> (which resolves <c>GetServices&lt;DbContext&gt;()</c>)
    /// discovers and wraps <em>every</em> module context — not just the first.
    /// </summary>
    /// <remarks>
    /// <b>Does not register <see cref="IUnitOfWork"/>.</b> In a modular monolith
    /// each module owns its own unit-of-work abstraction (see
    /// <c>Modulus.Core</c> apps: <c>{Module}.Application.IUnitOfWork</c>) and
    /// forwards it to its context in the module's composition root:
    /// <code>
    /// services.AddModuleDatabase&lt;CatalogDbContext&gt;(configure);
    /// services.AddScoped&lt;IUnitOfWork&gt;(sp =&gt; sp.GetRequiredService&lt;CatalogDbContext&gt;());
    /// </code>
    /// This keeps modules fully encapsulated: their <c>IUnitOfWork</c> types are
    /// distinct, so multiple modules never race for a single registration (the
    /// old last-wins behaviour silently dropped commits for all but the last
    /// module). The framework's <see cref="IUnitOfWork"/> is satisfied by
    /// <see cref="ModuleDbContext"/> (via <see cref="ModuleDbContext.CommitAsync"/>)
    /// and may be registered the same way for single-module apps.
    /// </remarks>
    public static IServiceCollection AddModuleDatabase<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configure)
        where TContext : ModuleDbContext
    {
        services.AddDbContext<TContext>(configure);

        // Also register as DbContext so TransactionBehavior (which resolves
        // GetServices<DbContext>()) discovers every module context and wraps
        // them all in a transaction — not just the first one. This is also how
        // EfRepository<T> locates the context that owns entity T.
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());

        // Record that TContext owns its entities so EfRepository<T> resolves
        // exactly this context for them (registration-time routing) instead of
        // instantiating and model-scanning every registered context per call.
        GetOrAddEntityContextMapRegistry(services).Register(typeof(TContext));

        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped(typeof(IReadRepository<>), typeof(EfReadRepository<>));
        return services;
    }

    /// <summary>
    /// Registers a module <typeparamref name="TContext"/> with a connection
    /// string resolved per-request via <paramref name="resolveConnectionString"/>.
    /// Use this overload when a single context type must route to different
    /// databases per tenant — the factory is invoked once per context creation,
    /// so a tenant-scoped resolver (reading the ambient tenant) picks the
    /// correct connection string for each request.
    /// </summary>
    /// <remarks>
    /// The context is registered with <c>optionsLifetime: Scoped</c> so that
    /// EF Core rebuilds <c>DbContextOptions</c> on every scope — the
    /// default singleton lifetime would cache the first tenant's connection
    /// string and return it for all subsequent tenants.
    /// </remarks>
    public static IServiceCollection AddModuleDatabase<TContext>(
        this IServiceCollection services,
        Func<IServiceProvider, string> resolveConnectionString,
        Action<DbContextOptionsBuilder, string>? configure = null)
        where TContext : ModuleDbContext
    {
        services.AddDbContext<TContext>(
            (sp, options) =>
            {
                var connectionString = resolveConnectionString(sp);
                configure?.Invoke(options, connectionString);
            },
            optionsLifetime: ServiceLifetime.Scoped);

        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        GetOrAddEntityContextMapRegistry(services).Register(typeof(TContext));
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped(typeof(IReadRepository<>), typeof(EfReadRepository<>));
        return services;
    }

    /// <summary>
    /// The single <see cref="EntityContextMapRegistry"/> shared across all
    /// <c>AddModuleDatabase&lt;TContext&gt;</c> calls, creating and registering
    /// it (and the <see cref="IEntityContextMap"/> that reads it) on first use.
    /// </summary>
    private static EntityContextMapRegistry GetOrAddEntityContextMapRegistry(
        IServiceCollection services)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(EntityContextMapRegistry))
                ?.ImplementationInstance is EntityContextMapRegistry existing)
            return existing;

        var registry = new EntityContextMapRegistry();
        services.AddSingleton(registry);
        services.TryAddSingleton<IEntityContextMap, EntityContextMap>();
        return registry;
    }
}
