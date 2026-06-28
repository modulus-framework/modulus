namespace Modulus.Data.SQLite;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.EntityFrameworkCore.Extensions;

public static class SQLiteExtensions
{
    /// <summary>
    /// File-based SQLite (persists between runs).
    /// NOT for production use.
    /// </summary>
    public static IServiceCollection AddSQLiteDatabase<TContext>(
        this IServiceCollection services,
        string dataSource = "./dev.db")
        where TContext : ModuleDbContext
    {
        services.AddModuleDatabase<TContext>(
            opts => opts.UseSqlite($"Data Source={dataSource}"));

        services.TryAddScoped(
            typeof(IModuleHealthCheck),
            typeof(SQLiteHealthCheck<>).MakeGenericType(typeof(TContext)));

        return services;
    }

    /// <summary>
    /// In-memory SQLite with a shared cache so the schema persists across
    /// pooled connections within the same process. Keeps a keep-alive
    /// connection open for the lifetime of the application so the in-memory
    /// DB is not destroyed when the last connection returns to the pool.
    /// NOT for production use.
    /// </summary>
    public static IServiceCollection AddSQLiteInMemory<TContext>(
        this IServiceCollection services)
        where TContext : ModuleDbContext
    {
        var connectionString =
            "Data Source=modulus_shared;Mode=Memory;Cache=Shared";

        services.AddModuleDatabase<TContext>(
            opts => opts.UseSqlite(connectionString));

        // Keep-alive singleton connection — prevents the shared in-memory DB
        // from being destroyed when all pooled connections are returned.
        services.AddKeyedSingleton<SqliteConnection>(
            "sqlite-keepalive",
            (_, _) =>
            {
                var conn = new SqliteConnection(connectionString);
                conn.Open();
                return conn;
            });

        return services;
    }
}