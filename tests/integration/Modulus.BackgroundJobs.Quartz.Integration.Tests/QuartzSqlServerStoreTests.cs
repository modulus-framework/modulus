namespace Modulus.BackgroundJobs.Quartz.Integration.Tests;

using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using global::Quartz;

[Collection(QuartzSqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class QuartzSqlServerStoreTests(QuartzSqlServerFixture fixture)
{
    [Fact]
    public async Task Schema_creates_every_table_and_is_idempotent()
    {
        var connectionString = await CreateDatabaseAsync();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await QuartzSchema.CreateSqlServerAsync(connection);
        await QuartzSchema.CreateSqlServerAsync(connection);

        (await CountQuartzTablesAsync(connection)).Should().Be(11);
    }

    [Fact]
    public async Task Scheduler_uses_the_persistent_store()
    {
        var connectionString = await CreateDatabaseAsync();
        await CreateSchemaAsync(connectionString);

        await using var provider = BuildProvider(connectionString);
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        (await scheduler.GetMetaData()).JobStoreType.Name.Should().Contain("JobStoreTX");
        await scheduler.Shutdown();
    }

    [Fact]
    public async Task A_durable_job_survives_a_restart()
    {
        var connectionString = await CreateDatabaseAsync();
        await CreateSchemaAsync(connectionString);
        var key = new JobKey("nightly-cleanup", "maintenance");

        await using (var first = BuildProvider(connectionString))
        {
            var scheduler = await first.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await scheduler.AddJob(JobBuilder.Create<NoopJob>().WithIdentity(key).StoreDurably().Build(), replace: true);
            await scheduler.Shutdown();
        }

        await using var second = BuildProvider(connectionString);
        var restarted = await second.GetRequiredService<ISchedulerFactory>().GetScheduler();

        (await restarted.CheckExists(key)).Should().BeTrue();
        await restarted.Shutdown();
    }

    // Each test gets its own database inside the shared container, so tests cannot see each other's tables.
    private async Task<string> CreateDatabaseAsync()
    {
        var database = "q" + Guid.NewGuid().ToString("N");
        var master = new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{database}]";
        await command.ExecuteNonQueryAsync();

        return new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = database }.ConnectionString;
    }

    private static async Task CreateSchemaAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await QuartzSchema.CreateSqlServerAsync(connection);
    }

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddQuartzJobScheduler(q => q.UseModulusSqlServerStore(connectionString));
        return services.BuildServiceProvider();
    }

    private static async Task<int> CountQuartzTablesAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = SCHEMA_NAME() AND TABLE_NAME LIKE 'QRTZ[_]%'";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed class NoopJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
