using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Modulus.BackgroundJobs;
using Modulus.Tenancy.Lifecycle;
using NSubstitute;
using Quartz;
using Xunit;

namespace Modulus.BackgroundJobs.Quartz.Tests;

/// <summary>The tenant retention sweep on a real Quartz scheduler with the persistent (SQLite) store, not a recording scheduler.</summary>
[Trait("Category", "Unit")]
public sealed class QuartzRetentionJobTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"modulus-quartz-retention-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={_path}";

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
            File.Delete(_path);
    }

    [Fact]
    public async Task The_schedule_is_stored_and_the_job_runs_the_sweep_when_it_fires()
    {
        await using (var connection = new SqliteConnection(ConnectionString))
            await QuartzSchema.CreateSqliteAsync(connection);

        var swept = new SemaphoreSlim(0);
        var provider = Substitute.For<ITenantLifecycleProvider>();
        provider.RunRetentionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            swept.Release();
            return Task.CompletedTask;
        });

        await using var sp = Build(provider);
        var scheduler = await sp.GetRequiredService<ISchedulerFactory>().GetScheduler();
        sp.GetRequiredService<IJobScheduler>().AddRecurring<TenantRetentionJob, TenantRetentionArgs>(TenantRetentionJob.JobId, "* * * * * ?", new TenantRetentionArgs());

        var key = new JobKey(TenantRetentionJob.JobId, "recurring");
        for (var i = 0; i < 100 && !await scheduler.CheckExists(key); i++)
            await Task.Delay(20);
        (await scheduler.CheckExists(key)).Should().BeTrue("the schedule is written to the database");

        await scheduler.Start();
        (await swept.WaitAsync(TimeSpan.FromSeconds(15))).Should().BeTrue("the persistent trigger fired the retention sweep");
        await scheduler.Shutdown();

        // A restart finds the same schedule in the database; registering it again does not duplicate it.
        await using var restarted = Build(provider);
        var again = await restarted.GetRequiredService<ISchedulerFactory>().GetScheduler();
        (await again.CheckExists(key)).Should().BeTrue();
        restarted.GetRequiredService<IJobScheduler>().AddRecurring<TenantRetentionJob, TenantRetentionArgs>(TenantRetentionJob.JobId, "* * * * * ?", new TenantRetentionArgs());
        await Task.Delay(300);
        (await again.GetTriggersOfJob(key)).Should().HaveCount(1);
        await again.Shutdown();
    }

    private ServiceProvider Build(ITenantLifecycleProvider provider)
    {
        var services = new ServiceCollection();
        services.AddSilentLogging();
        services.AddSingleton(provider);
        services.AddTransient<TenantRetentionJob>();
        services.AddQuartzJobScheduler(q => q.UseModulusSqliteStore(ConnectionString));
        return services.BuildServiceProvider();
    }
}
