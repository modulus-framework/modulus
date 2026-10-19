namespace Modulus.Data.PostgreSQL;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.EntityFrameworkCore;
using Modulus.EntityFrameworkCore.Isolation;

/// <summary>Registers a module context with one PostgreSQL database per tenant.</summary>
public static class PostgreSqlTenantDatabaseExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TContext"/> on PostgreSQL with one database per tenant, read from the tenant data-store
    /// catalog under <paramref name="storeKey"/>. The host connection serves the host context. See
    /// <c>AddModuleDatabasePerTenant</c> for the requirements.
    /// </summary>
    /// <typeparam name="TContext">The module context.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="storeKey">The tenant store (for example <c>primary</c>).</param>
    /// <param name="hostConnectionString">The host database connection string.</param>
    public static IServiceCollection AddPostgreSqlPerTenantDatabase<TContext>(
        this IServiceCollection services, string storeKey, string hostConnectionString)
        where TContext : ModuleDbContext
        => services.AddModuleDatabasePerTenant<TContext>(
            storeKey, "postgresql", hostConnectionString, (options, connectionString) => options.UseNpgsql(connectionString));
}
