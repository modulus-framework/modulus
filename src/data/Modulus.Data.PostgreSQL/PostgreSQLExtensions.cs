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

        // TryAddEnumerable, NOT TryAddScoped: TryAdd* keys on the *service* type,
        // so in a multi-module app the second AddPostgreSQLDatabase<T> call would
        // find IModuleHealthCheck already registered and add nothing — only the
        // first module's database would ever be health-checked, and /health/ready
        // would report healthy while another module's database was down.
        // TryAddEnumerable keys on the implementation type, which is distinct per
        // TContext, so every module contributes its own check exactly once.
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IModuleHealthCheck, RelationalDatabaseHealthCheck<TContext>>(
                sp => new RelationalDatabaseHealthCheck<TContext>(
                    sp.GetRequiredService<TContext>(), "postgresql")));

        return services;
    }
}
