namespace Modulus.Data.PostgreSQL;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.EntityFrameworkCore.Health;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

public static class PostgreSQLExtensions
{
    public static IServiceCollection AddPostgreSQLDatabase<TContext>(
        this IServiceCollection services,
        string connectionString,
        Action<NpgsqlDbContextOptionsBuilder>? configure = null,
        bool useSnakeCaseNaming = true)
        where TContext : ModuleDbContext
    {
        services.AddModuleDatabase<TContext>(opts =>
        {
            opts.UseNpgsql(connectionString, pg =>
            {
                pg.EnableRetryOnFailure(3);
                configure?.Invoke(pg);
            });

            if (useSnakeCaseNaming)
                opts.UseSnakeCaseNamingConvention();
        });

        services.TryAddScoped<IModuleHealthCheck>(sp =>
            new RelationalDatabaseHealthCheck<TContext>(
                sp.GetRequiredService<TContext>(), "postgresql"));

        return services;
    }
}
