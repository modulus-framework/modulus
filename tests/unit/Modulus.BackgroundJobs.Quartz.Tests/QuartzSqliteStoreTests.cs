using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.BackgroundJobs.Quartz;
using Quartz;
using Xunit;

namespace Modulus.BackgroundJobs.Quartz.Tests;

[Trait("Category", "Unit")]
public sealed class QuartzSqliteStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"modulus-quartz-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={_path}";

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
            File.Delete(_path);
    }

    [Fact]
    public async Task Schema_creates_every_table_on_an_empty_database_and_is_idempotent()
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await QuartzSchema.CreateSqliteAsync(connection);
        await QuartzSchema.CreateSqliteAsync(connection);

        (await CountQuartzTablesAsync(connection)).Should().Be(11);
    }

    [Fact]
    public async Task Schema_refuses_a_partly_created_database()
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE QRTZ_JOB_DETAILS (SCHED_NAME TEXT NOT NULL, JOB_NAME TEXT NOT NULL, JOB_GROUP TEXT NOT NULL, PRIMARY KEY (SCHED_NAME, JOB_NAME, JOB_GROUP))";
            await command.ExecuteNonQueryAsync();
        }

        var act = () => QuartzSchema.CreateSqliteAsync(connection);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Scheduler_uses_the_persistent_store_not_the_in_memory_one()
    {
        await using (var connection = new SqliteConnection(ConnectionString))
            await QuartzSchema.CreateSqliteAsync(connection);

        await using var provider = BuildProvider();
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        var metadata = await scheduler.GetMetaData();
        metadata.JobStoreType.Name.Should().Contain("JobStoreTX");
        await scheduler.Shutdown();
    }

    [Fact]
    public async Task A_durable_job_survives_a_restart()
    {
        var key = new JobKey("nightly-cleanup", "maintenance");
        await using (var connection = new SqliteConnection(ConnectionString))
            await QuartzSchema.CreateSqliteAsync(connection);

        await using (var first = BuildProvider())
        {
            var scheduler = await first.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await scheduler.AddJob(
                JobBuilder.Create<NoopJob>().WithIdentity(key).StoreDurably().Build(),
                replace: true);
            await scheduler.Shutdown();
        }

        await using var second = BuildProvider();
        var restarted = await second.GetRequiredService<ISchedulerFactory>().GetScheduler();

        (await restarted.CheckExists(key)).Should().BeTrue();
        await restarted.Shutdown();
    }

    [Fact]
    public void Default_registration_uses_the_in_memory_store()
    {
        var services = new ServiceCollection();
        services.AddSilentLogging();
        services.AddQuartzJobScheduler();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<QuartzOptions>>().Value["quartz.jobStore.type"]
            .Should().Contain("RAMJobStore");
    }

    [Fact]
    public void Sqlite_registration_records_the_adonet_store_for_the_hosted_service()
    {
        using var provider = BuildProvider();

        provider.GetRequiredService<IOptions<QuartzOptions>>().Value["quartz.jobStore.type"]
            .Should().Contain("JobStoreTX");
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSilentLogging();
        services.AddQuartzJobScheduler(q => q.UseModulusSqliteStore(ConnectionString));
        return services.BuildServiceProvider();
    }

    private static async Task<int> CountQuartzTablesAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name LIKE 'QRTZ\\_%' ESCAPE '\\'";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed class NoopJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
