using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Modulus.BackgroundJobs.Quartz;

/// <summary>
/// Registration extensions for Quartz.NET-backed job scheduling.
/// Replaces the in-memory ChannelJobQueue with Quartz.NET, which provides
/// durability via a configurable job store and built-in clustering support.
/// </summary>
public static class QuartzExtensions
{
    /// <summary>
    /// Registers Quartz.NET as the background job scheduler.
    /// <para>
    /// By default, this uses an in-memory store suitable for dev/test. Jobs queued in memory are lost on restart. For
    /// production, call <see cref="UseModulusSqliteStore"/> or <see cref="UseModulusPostgresStore"/> on the configurator to
    /// persist jobs to a database. The hosted service logs a warning at startup when a Production host runs in memory.
    /// </para>
    /// </summary>
    /// <param name="services">Service collection to register with.</param>
    /// <param name="configureQuartz">Optional action to customize Quartz configuration.</param>
    public static IServiceCollection AddQuartzJobScheduler(
        this IServiceCollection services,
        Action<IServiceCollectionQuartzConfigurator>? configureQuartz = null)
    {
        services.AddQuartz(q =>
        {
            q.UseInMemoryStore();
            q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 10);

            configureQuartz?.Invoke(q);
        });

        services.AddHostedService<QuartzHostedService>();
        services.AddScoped<IJobScheduler, QuartzJobScheduler>();

        return services;
    }

    /// <summary>
    /// Stores Quartz jobs and triggers in a SQLite database (Microsoft.Data.Sqlite), so scheduled work survives a restart.
    /// Apply the schema first with <see cref="QuartzSchema.CreateSqliteAsync"/>. Use it for a single node or development;
    /// several replicas need a shared database and clustering (<c>UseClustering</c>), which this does not turn on.
    /// </summary>
    /// <param name="configurator">The Quartz configurator passed to <c>AddQuartzJobScheduler</c>.</param>
    /// <param name="connectionString">A Microsoft.Data.Sqlite connection string for the job store database.</param>
    public static IServiceCollectionQuartzConfigurator UseModulusSqliteStore(
        this IServiceCollectionQuartzConfigurator configurator,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Quartz needs a serializer for any persistent store. Job arguments are arbitrary objects, so binary is not an option.
        configurator.UsePersistentStore(store =>
        {
            store.UseMicrosoftSQLite(connectionString);
            store.UseSystemTextJsonSerializer();
        });
        return configurator;
    }

    /// <summary>
    /// Stores Quartz jobs and triggers in PostgreSQL (Npgsql), so scheduled work survives a restart and is shared by
    /// the replicas that point at the same database. Apply the schema first with <see cref="QuartzSchema.CreatePostgresAsync"/>.
    /// Several replicas also need clustering (<c>UseClustering</c>), which this does not turn on.
    /// </summary>
    /// <param name="configurator">The Quartz configurator passed to <c>AddQuartzJobScheduler</c>.</param>
    /// <param name="connectionString">An Npgsql connection string for the job store database.</param>
    /// <param name="clustered">
    /// True when several replicas share this database: each job fires on one node only, and a node that stops is
    /// detected by the others and its work is taken over.
    /// </param>
    public static IServiceCollectionQuartzConfigurator UseModulusPostgresStore(
        this IServiceCollectionQuartzConfigurator configurator,
        string connectionString,
        bool clustered = false)
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        configurator.UsePersistentStore(store =>
        {
            store.UsePostgres(connectionString);
            store.UseSystemTextJsonSerializer();
            if (clustered)
                store.UseClustering();
        });
        return configurator;
    }

    /// <summary>
    /// Stores Quartz jobs and triggers in SQL Server. Apply the schema first with <see cref="QuartzSchema.CreateSqlServerAsync"/>.
    /// The application must load <c>Microsoft.Data.SqlClient</c> (Entity Framework Core's SQL Server provider does); the Quartz
    /// package does not bring it. Several replicas also need clustering (<c>UseClustering</c>), which this does not turn on.
    /// </summary>
    /// <param name="configurator">The Quartz configurator passed to <c>AddQuartzJobScheduler</c>.</param>
    /// <param name="connectionString">A SQL Server connection string for the job store database.</param>
    /// <param name="clustered">True when several replicas share this database (see <see cref="UseModulusPostgresStore"/>).</param>
    public static IServiceCollectionQuartzConfigurator UseModulusSqlServerStore(
        this IServiceCollectionQuartzConfigurator configurator,
        string connectionString,
        bool clustered = false)
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        configurator.UsePersistentStore(store =>
        {
            store.UseSqlServer(connectionString);
            store.UseSystemTextJsonSerializer();
            if (clustered)
                store.UseClustering();
        });
        return configurator;
    }
}
