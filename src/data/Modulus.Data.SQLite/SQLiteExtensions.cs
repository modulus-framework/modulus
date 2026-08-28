namespace Modulus.Data.SQLite;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.EntityFrameworkCore.Health;

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

        services.TryAddScoped<IModuleHealthCheck>(sp =>
            new RelationalDatabaseHealthCheck<TContext>(
                sp.GetRequiredService<TContext>(), "sqlite"));

        return services;
    }

    /// <summary>
    /// In-memory SQLite with a per-context shared cache so the schema persists
    /// across pooled connections within the same process. Keeps a keep-alive
    /// connection open for the lifetime of the application so the in-memory
    /// DB is not destroyed when the last pooled connection returns.
    /// NOT for production use.
    /// </summary>
    /// <remarks>
    /// Each context gets its OWN uniquely-named in-memory database. Sharing one
    /// name across modules makes <c>EnsureCreated</c>/<c>Migrate</c> on the
    /// second+ contexts no-ops against another module's schema — silent data
    /// loss. The database name derives from the context type, so two modules
    /// stay isolated while remaining pool- and restart-stable within a run.
    /// </remarks>
    public static IServiceCollection AddSQLiteInMemory<TContext>(
        this IServiceCollection services)
        where TContext : ModuleDbContext
    {
        // Unique per registered context; SQLite treats it as an arbitrary name
        // mapped by Cache=Shared within this process.
        var connectionString =
            $"Data Source={typeof(TContext).Name}_shared;Mode=Memory;Cache=Shared";

        services.AddModuleDatabase<TContext>(
            opts => opts.UseSqlite(connectionString));

        // Keep-alive hosted service — holds ONE open connection for the app's
        // lifetime. Lazily-created keyed singletons were never resolved when no
        // code touched them, so the in-memory DB died with the connection pool.
        services.AddHostedService(_ =>
            new SqliteKeepAliveService(connectionString));

        return services;
    }

    /// <summary>
    /// Opens one process-local connection at startup and closes it only when
    /// the host shuts down, pinning the shared-cache in-memory database in
    /// memory regardless of request traffic or pooling behaviour.
    /// </summary>
    internal sealed class SqliteKeepAliveService(string connectionString)
        : IHostedService, IDisposable
    {
        private SqliteConnection? _keepAlive;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _keepAlive = new SqliteConnection(connectionString);
            await _keepAlive.OpenAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public void Dispose() => _keepAlive?.Dispose();
    }
}
