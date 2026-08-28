namespace Modulus.Data.SqlServer;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.EntityFrameworkCore.Health;

public static class SqlServerExtensions
{
    public static IServiceCollection AddSqlServerDatabase<TContext>(
        this IServiceCollection services,
        string connectionString,
        Action<SqlServerDbContextOptionsBuilder>? configure = null)
        where TContext : ModuleDbContext
    {
        services.AddModuleDatabase<TContext>(opts =>
            opts.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(3);
                configure?.Invoke(sql);
            }));

        services.TryAddScoped<IModuleHealthCheck>(sp =>
            new RelationalDatabaseHealthCheck<TContext>(
                sp.GetRequiredService<TContext>(), "sqlserver"));

        return services;
    }
}
