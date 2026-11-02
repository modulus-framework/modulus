namespace Modulus.Tenancy.Lifecycle.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>Registers the Entity Framework Core lifecycle store.</summary>
public static class TenantLifecycleEntityFrameworkCoreExtensions
{
    /// <summary>
    /// Stores the lifecycle in <typeparamref name="TContext"/>, which the application registers (for example with <c>AddDbContext</c>).
    /// It is also registered as a <see cref="DbContext"/>, so <c>MigrateModulusDatabasesAsync</c> migrates it with the modules.
    /// Replaces the in-memory store; registration order against <c>AddTenantLifecycle</c> does not matter.
    /// </summary>
    public static IServiceCollection AddEfTenantLifecycleStore<TContext>(this IServiceCollection services)
        where TContext : TenantLifecycleDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.RemoveAll<ITenantLifecycleStore>();
        services.AddScoped<ITenantLifecycleStore, EfTenantLifecycleStore<TContext>>();
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        return services;
    }
}
