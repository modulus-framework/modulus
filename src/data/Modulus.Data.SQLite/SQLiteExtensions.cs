namespace Modulus.Data.SQLite;

using Microsoft.EntityFrameworkCore;
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
        => services.AddModuleDatabase<TContext>(
            opts => opts.UseSqlite($"Data Source={dataSource}"));

    /// <summary>
    /// In-memory SQLite for integration tests.
    /// Each call creates a separate database.
    /// </summary>
    public static IServiceCollection AddSQLiteInMemory<TContext>(
        this IServiceCollection services)
        where TContext : ModuleDbContext
        => services.AddModuleDatabase<TContext>(
            opts => opts.UseSqlite("Data Source=:memory:"));
}