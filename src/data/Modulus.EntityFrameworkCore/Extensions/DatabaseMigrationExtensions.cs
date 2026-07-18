namespace Modulus.EntityFrameworkCore.Extensions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Controls how <see cref="DatabaseMigrationExtensions.MigrateModulusDatabasesAsync"/>
/// brings each module database to the current schema on startup.
/// </summary>
public enum DatabaseInitializationMode
{
    /// <summary>
    /// Apply EF Core migrations when the context defines any; otherwise fall back
    /// to <c>EnsureCreatedAsync</c>. Convenient in <b>Development</b> so freshly
    /// generated apps run before any migrations are authored, and start using
    /// migrations automatically once you add the first one with
    /// <c>modulus migrate add</c>. Do not use in production and do not mix the two
    /// on one database — <c>EnsureCreated</c> does not write the migrations-history
    /// table, so a later <c>Migrate</c> cannot pick up where it left off.
    /// </summary>
    MigrateOrCreate,

    /// <summary>
    /// Always apply migrations. Throws if the context defines none. <b>This is the
    /// default</b> — production must bring every schema change through a migration,
    /// and failing fast on a context with no migrations surfaces the mistake at
    /// startup rather than silently creating an un-migratable schema.
    /// </summary>
    Migrate,

    /// <summary>
    /// Only <c>EnsureCreatedAsync</c> —
    /// a one-shot schema snapshot with no migration history. Prototyping/tests only.
    /// </summary>
    EnsureCreated,
}

/// <summary>
/// Startup helpers that initialise every module's database. Each module owns its
/// own <see cref="DbContext"/> (registered via <c>AddModuleDatabase</c> and also
/// exposed as <see cref="DbContext"/>), so these resolve <em>all</em> of them and
/// bring each to the current schema.
/// </summary>
public static class DatabaseMigrationExtensions
{
    /// <summary>
    /// Initialises every registered module <see cref="DbContext"/> according to
    /// <paramref name="mode"/>. Replaces hand-rolled <c>EnsureCreated</c> loops in
    /// generated <c>Program.cs</c> files.
    /// </summary>
    /// <remarks>
    /// Each context is driven through its own execution strategy
    /// (<c>CreateExecutionStrategy().ExecuteAsync(...)</c>) so that connection
    /// resilience (<c>EnableRetryOnFailure</c>) applies to the migration/creation
    /// step too — a transient failure while opening the connection is retried
    /// rather than crashing startup. A fresh scope is created so the work does not
    /// borrow a request scope.
    /// </remarks>
    public static async Task MigrateModulusDatabasesAsync(
        this IServiceProvider services,
        DatabaseInitializationMode mode = DatabaseInitializationMode.Migrate,
        CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("Modulus.Database");

        foreach (var db in sp.GetServices<DbContext>())
        {
            var name = db.GetType().Name;
            var strategy = db.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                switch (mode)
                {
                    case DatabaseInitializationMode.EnsureCreated:
                        await db.Database.EnsureCreatedAsync(ct);
                        logger?.LogInformation(
                            "Ensured schema for {Context} (EnsureCreated).", name);
                        break;

                    case DatabaseInitializationMode.Migrate:
                        await db.Database.MigrateAsync(ct);
                        logger?.LogInformation(
                            "Applied migrations for {Context}.", name);
                        break;

                    default: // MigrateOrCreate
                        // GetMigrations() reflects the migrations compiled into the
                        // context assembly (no DB round-trip); an empty result means
                        // none have been authored yet.
                        if (db.Database.GetMigrations().Any())
                        {
                            await db.Database.MigrateAsync(ct);
                            logger?.LogInformation(
                                "Applied migrations for {Context}.", name);
                        }
                        else
                        {
                            await db.Database.EnsureCreatedAsync(ct);
                            logger?.LogInformation(
                                "No migrations for {Context}; created schema via " +
                                "EnsureCreated. Run 'modulus migrate add <Name>' to " +
                                "switch this module to migrations.", name);
                        }
                        break;
                }
            });
        }
    }
}
