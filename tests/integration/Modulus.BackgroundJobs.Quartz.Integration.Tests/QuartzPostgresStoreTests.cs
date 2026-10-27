namespace Modulus.BackgroundJobs.Quartz.Integration.Tests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using global::Quartz;
using Xunit;

[Collection(QuartzPostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class QuartzPostgresStoreTests(QuartzPostgresFixture fixture)
{
    [Fact]
    public async Task Schema_creates_every_table_and_is_idempotent()
    {
        var schema = Unique();
        await using var connection = await OpenAsync(schema);

        await QuartzSchema.CreatePostgresAsync(connection);
        await QuartzSchema.CreatePostgresAsync(connection);

        (await CountQuartzTablesAsync(connection)).Should().Be(11);
    }

    [Fact]
    public async Task Scheduler_uses_the_persistent_store()
    {
        var schema = Unique();
        await using (var connection = await OpenAsync(schema))
            await QuartzSchema.CreatePostgresAsync(connection);

        await using var provider = BuildProvider(schema);
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        (await scheduler.GetMetaData()).JobStoreType.Name.Should().Contain("JobStoreTX");
        await scheduler.Shutdown();
    }

    [Fact]
    public async Task A_durable_job_survives_a_restart()
    {
        var schema = Unique();
        var key = new JobKey("nightly-cleanup", "maintenance");
        await using (var connection = await OpenAsync(schema))
            await QuartzSchema.CreatePostgresAsync(connection);

        await using (var first = BuildProvider(schema))
        {
            var scheduler = await first.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await scheduler.AddJob(JobBuilder.Create<NoopJob>().WithIdentity(key).StoreDurably().Build(), replace: true);
            await scheduler.Shutdown();
        }

        await using var second = BuildProvider(schema);
        var restarted = await second.GetRequiredService<ISchedulerFactory>().GetScheduler();

        (await restarted.CheckExists(key)).Should().BeTrue();
        await restarted.Shutdown();
    }

    [Fact]
    public async Task A_clustered_node_checks_in_to_the_shared_store()
    {
        var schema = Unique();
        await using (var connection = await OpenAsync(schema))
            await QuartzSchema.CreatePostgresAsync(connection);

        await using var provider = BuildProvider(schema, clustered: true);
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();

        // Clustered nodes record themselves in QRTZ_SCHEDULER_STATE; a non-clustered store writes no rows.
        (await CountCheckedInNodesAsync(schema)).Should().Be(1);
        await scheduler.Shutdown();
    }

    [Fact]
    public async Task A_non_clustered_store_records_no_node()
    {
        var schema = Unique();
        await using (var connection = await OpenAsync(schema))
            await QuartzSchema.CreatePostgresAsync(connection);

        await using var provider = BuildProvider(schema, clustered: false);
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();

        (await CountCheckedInNodesAsync(schema)).Should().Be(0);
        await scheduler.Shutdown();
    }

    // Each test gets its own PostgreSQL schema inside the shared container, so the tests cannot see each other's tables.
    private static string Unique() => "q" + Guid.NewGuid().ToString("N");

    private async Task<NpgsqlConnection> OpenAsync(string schema)
    {
        var connection = new NpgsqlConnection(ConnectionStringFor(schema));
        await connection.OpenAsync();
        await using var create = connection.CreateCommand();
        create.CommandText = $"CREATE SCHEMA {schema}";
        await create.ExecuteNonQueryAsync();
        return connection;
    }

    private string ConnectionStringFor(string schema) =>
        new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { SearchPath = schema }.ConnectionString;

    private ServiceProvider BuildProvider(string schema, bool clustered = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddQuartzJobScheduler(q => q.UseModulusPostgresStore(ConnectionStringFor(schema), clustered));
        return services.BuildServiceProvider();
    }

    private async Task<int> CountCheckedInNodesAsync(string schema)
    {
        await using var connection = new NpgsqlConnection(ConnectionStringFor(schema));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM qrtz_scheduler_state";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountQuartzTablesAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name LIKE 'qrtz%'";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed class NoopJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
