namespace Modulus.EntityFrameworkCore.Isolation;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore.Extensions;

/// <summary>How a module context keeps companies apart below the EF Core query filter.</summary>
public enum TenantIsolationMode
{
    /// <summary>Shared tables guarded by the EF query filter, the write guard and the raw-SQL guard only.</summary>
    QueryFilter,

    /// <summary>Shared tables plus database row-level security fed by the session context (PostgreSQL, SQL Server).</summary>
    RowLevelSecurity,

    /// <summary>One database per tenant; the connection is chosen from the ambient tenant.</summary>
    DatabasePerTenant,
}

/// <summary>A module context's declared isolation, with the session interceptor that implements it, if any.</summary>
/// <param name="ContextType">The module context.</param>
/// <param name="Mode">How it is isolated.</param>
/// <param name="SessionInterceptor">Writes the tenant into the database session (row-level security only).</param>
public sealed record ModuleDbContextIsolation(
    Type ContextType,
    TenantIsolationMode Mode,
    TenantSessionInterceptor? SessionInterceptor = null);

/// <summary>Registration helpers for database-level tenant isolation.</summary>
public static class DataIsolationExtensions
{
    /// <summary>
    /// Declares that <typeparamref name="TContext"/> relies on row-level security and adds the provider's
    /// session interceptor to it. Called by the provider packages (<c>AddPostgreSqlRowLevelSecurity</c>,
    /// <c>AddSqlServerRowLevelSecurity</c>); order relative to <c>AddModuleDatabase</c> does not matter.
    /// </summary>
    /// <typeparam name="TContext">The module context.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="interceptor">The provider's session interceptor (one shared instance is fine).</param>
    public static IServiceCollection AddTenantSessionContext<TContext>(
        this IServiceCollection services, TenantSessionInterceptor interceptor)
        where TContext : ModuleDbContext
    {
        ArgumentNullException.ThrowIfNull(interceptor);
        if (!services.Any(d => d.ImplementationInstance is ModuleDbContextIsolation i && i.ContextType == typeof(TContext)))
            services.AddSingleton(new ModuleDbContextIsolation(typeof(TContext), TenantIsolationMode.RowLevelSecurity, interceptor));
        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TContext"/> with one database per tenant: the ambient tenant picks
    /// <paramref name="tenantConnectionString"/>, the host context <paramref name="hostConnectionString"/>, and
    /// no tenant at all throws (it never falls back to the host database).
    /// </summary>
    /// <typeparam name="TContext">The module context.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="hostConnectionString">The database of the host context (migrations, tenant catalogue jobs).</param>
    /// <param name="tenantConnectionString">The database of a tenant.</param>
    /// <param name="configure">Applies the provider (<c>(o, cs) =&gt; o.UseNpgsql(cs)</c>).</param>
    public static IServiceCollection AddModuleDatabasePerTenant<TContext>(
        this IServiceCollection services,
        string hostConnectionString,
        Func<Guid, string> tenantConnectionString,
        Action<DbContextOptionsBuilder, string> configure)
        where TContext : ModuleDbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostConnectionString);
        ArgumentNullException.ThrowIfNull(tenantConnectionString);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddModuleDatabase<TContext>(
            sp => ResolveConnectionString(sp.GetRequiredService<ICurrentTenant>(), hostConnectionString, tenantConnectionString, typeof(TContext)),
            configure);
        services.AddSingleton(new ModuleDbContextIsolation(typeof(TContext), TenantIsolationMode.DatabasePerTenant));
        return services;
    }

    /// <summary>
    /// Checks at startup that every module context with tenant tables meets <c>Security:DataIsolation:Tier</c>
    /// (<c>Shared</c> or <c>DatabasePerTenant</c>) and logs each context's isolation. Nothing is checked while the
    /// setting is absent.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    public static IServiceCollection AddModulusDataIsolationCheck(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, DataIsolationCheck>(
            sp => ActivatorUtilities.CreateInstance<DataIsolationCheck>(sp, configuration)));
        return services;
    }

    internal static string ResolveConnectionString(
        ICurrentTenant tenant, string host, Func<Guid, string> perTenant, Type contextType)
    {
        if (tenant.IsHost)
            return host;
        return tenant.TenantId is { } id
            ? perTenant(id)
            : throw new InvalidOperationException(
                $"{contextType.Name} keeps one database per tenant, and no tenant is in scope. Resolve a tenant, or "
                + "enter the host context (ICurrentTenant.Change(null)) for host work.");
    }
}
