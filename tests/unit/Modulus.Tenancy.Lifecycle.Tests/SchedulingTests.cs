namespace Modulus.Tenancy.Lifecycle.Tests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.BackgroundJobs;
using Xunit;

[Trait("Category", "Unit")]
public sealed class SchedulingTests
{
    private static IHostedService ScheduleService(Harness harness) =>
        harness.Services.GetServices<IHostedService>().Single(s => s.GetType().Name == "TenantRetentionScheduleService");

    [Fact]
    public async Task The_worker_driver_is_the_default_and_schedules_nothing()
    {
        var scheduler = new RecordingScheduler();
        using var harness = new Harness(extra: s => s.AddSingleton<IJobScheduler>(scheduler));

        await ScheduleService(harness).StartAsync(default);

        scheduler.Recurring.Should().BeEmpty("the lifecycle's own worker runs the sweep by default");
    }

    [Fact]
    public async Task The_scheduler_driver_registers_the_sweep_with_the_configured_cron()
    {
        var scheduler = new RecordingScheduler();
        using var harness = new Harness(
            overrides: new Dictionary<string, string?>
            {
                ["TenantLifecycle:RetentionDriver"] = "Scheduler",
                ["TenantLifecycle:RetentionCron"] = "0 30 2 * * ?",
            },
            extra: s => s.AddSingleton<IJobScheduler>(scheduler));

        await ScheduleService(harness).StartAsync(default);

        scheduler.Recurring.Should().ContainSingle()
            .Which.Should().Be((TenantRetentionJob.JobId, "0 30 2 * * ?", typeof(TenantRetentionJob)));
    }

    [Fact]
    public async Task The_scheduler_driver_refuses_to_start_without_a_job_scheduler()
    {
        using var harness = new Harness(overrides: new Dictionary<string, string?> { ["TenantLifecycle:RetentionDriver"] = "Scheduler" });

        var act = () => ScheduleService(harness).StartAsync(default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*job scheduler*");
    }

    [Fact]
    public async Task The_scheduled_job_runs_the_retention_sweep()
    {
        using var harness = new Harness();
        harness.Seed();
        var provider = harness.Provider();
        var operation = await provider.RequestOffboardingAsync(Harness.Company, "operator", exportFirst: false);
        await provider.RunOperationAsync(operation.Id);
        harness.Clock.Advance(TimeSpan.FromDays(366));

        await harness.Services.CreateScope().ServiceProvider.GetRequiredService<TenantRetentionJob>()
            .ExecuteAsync(new TenantRetentionArgs(), default);

        harness.World.Deleted.Should().Contain(BillingHandler.Usage);
    }

    /// <summary>Records what the lifecycle schedules; the platform scheduler is not needed to test the registration.</summary>
    private sealed class RecordingScheduler : IJobScheduler
    {
        public List<(string Id, string Cron, Type Job)> Recurring { get; } = [];

        public Task EnqueueAsync<TJob, TArgs>(TArgs args, CancellationToken ct = default) where TJob : IBackgroundJob<TArgs> => Task.CompletedTask;

        public Task ScheduleAsync<TJob, TArgs>(TArgs args, TimeSpan delay, CancellationToken ct = default) where TJob : IBackgroundJob<TArgs> => Task.CompletedTask;

        public void AddRecurring<TJob, TArgs>(string jobId, string cronExpression, TArgs args, DateTime? firstRunAt = null)
            where TJob : IBackgroundJob<TArgs>
            => Recurring.Add((jobId, cronExpression, typeof(TJob)));

        public void RemoveRecurring(string jobId) { }
    }
}
