namespace Modulus.Data.SqlServer;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Modulus.EntityFrameworkCore;
using Modulus.EntityFrameworkCore.Extensions;

public static class SqlServerExtensions
{
    public static IServiceCollection AddSqlServerDatabase<TContext>(
        this IServiceCollection services,
        string connectionString,
        Action<SqlServerDbContextOptionsBuilder>? configure = null)
        where TContext : ModuleDbContext
        => services.AddModuleDatabase<TContext>(opts =>
            opts.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(3);
                configure?.Invoke(sql);
            }));
}