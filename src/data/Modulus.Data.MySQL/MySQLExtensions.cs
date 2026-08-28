namespace Modulus.Data.MySQL;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.EntityFrameworkCore.Health;
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

        services.TryAddScoped<IModuleHealthCheck>(sp =>
            new RelationalDatabaseHealthCheck<TContext>(
                sp.GetRequiredService<TContext>(), "mysql"));

        return services;
    }
}
