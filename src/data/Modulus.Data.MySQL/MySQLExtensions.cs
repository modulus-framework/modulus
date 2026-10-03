namespace Modulus.Data.MySQL;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.EntityFrameworkCore.Health;
using Modulus.EntityFrameworkCore.Isolation;
using MySql.EntityFrameworkCore.Extensions;
using MySql.EntityFrameworkCore.Infrastructure;

public static class MySQLExtensions
{
    public static IServiceCollection AddMySQLDatabase<TContext>(
        this IServiceCollection services,
        string connectionString,
        Action<MySQLDbContextOptionsBuilder>? configure = null)
        where TContext : ModuleDbContext
    {
        services.AddModuleDatabase<TContext>(opts =>
            opts.UseMySQL(connectionString, my =>
            {
                my.EnableRetryOnFailure(3);
                configure?.Invoke(my);
            }));

        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IModuleHealthCheck, RelationalDatabaseHealthCheck<TContext>>(
                sp => new RelationalDatabaseHealthCheck<TContext>(
                    sp.GetRequiredService<TContext>(), "mysql")));

        return services;
    }

    /// <summary>
    /// One MySQL database per tenant. MySQL has no row-level security, so a database per tenant is its
    /// recommended company boundary (a shared database is guarded by the EF filter and the raw-SQL guard only).
    /// The ambient tenant picks <paramref name="tenantConnectionString"/>, the host context
    /// <paramref name="hostConnectionString"/>; with no tenant in scope the context refuses to open.
    /// </summary>
    /// <remarks>No readiness check is registered: a probe request carries no tenant to pick a database with.</remarks>
    /// <typeparam name="TContext">The module context.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="hostConnectionString">The host context's database.</param>
    /// <param name="tenantConnectionString">A tenant's database.</param>
    /// <param name="configure">Provider options.</param>
    public static IServiceCollection AddMySQLPerTenantDatabase<TContext>(
        this IServiceCollection services,
        string hostConnectionString,
        Func<Guid, string> tenantConnectionString,
        Action<MySQLDbContextOptionsBuilder>? configure = null)
        where TContext : ModuleDbContext
        => services.AddModuleDatabasePerTenant<TContext>(
            hostConnectionString,
            tenantConnectionString,
            (opts, connectionString) => opts.UseMySQL(connectionString, my =>
            {
                my.EnableRetryOnFailure(3);
                configure?.Invoke(my);
            }));
}
