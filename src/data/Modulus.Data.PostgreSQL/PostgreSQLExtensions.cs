namespace Modulus.Data.PostgreSQL;

using Microsoft.EntityFrameworkCore;
using Modulus.EntityFrameworkCore;
using Modulus.EntityFrameworkCore.Extensions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

public static class PostgreSQLExtensions
{
    public static IServiceCollection AddPostgreSQLDatabase<TContext>(
        this IServiceCollection services,
        string connectionString,
        Action<NpgsqlDbContextOptionsBuilder>? configure = null)
        where TContext : ModuleDbContext
        => services.AddModuleDatabase<TContext>(opts =>
            opts.UseNpgsql(connectionString, pg =>
            {
                pg.EnableRetryOnFailure(3);
                configure?.Invoke(pg);
            }));
}