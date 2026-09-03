using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.BackgroundJobs;
using Modulus.Core.Abstractions;
using Modulus.Core.Correlation;
using Modulus.MultiTenancy;
using Quartz;
using Quartz.Impl.Matchers;
using Xunit;

namespace Modulus.BackgroundJobs.Quartz.Tests;

/// <summary>
/// Regression coverage for B3: <c>ScheduleAsync</c> (delayed) and
/// <c>AddRecurring</c> (cron) used to build their <see cref="JobDataMap"/>
/// with only <c>["args"]</c> — unlike <c>EnqueueAsync</c>, which also carried
/// <c>tenantId</c>/<c>correlationId</c>. <see cref="QuartzJobAdapter{TJob,TArgs}"/>
/// silently opens NO tenant scope when those keys are absent, so every
/// delayed or recurring job ran with no ambient tenant while immediate jobs
/// worked correctly — a cross-tenant data-isolation risk for any handler that
/// touches a tenant-scoped <c>ModuleDbContext</c>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class QuartzTenantContextTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        services.AddSingleton<ICorrelationContext, CorrelationContext>();
        // A unique instance name per test: Quartz.NET registers schedulers
        // into a process-wide static repository keyed by name, so two tests
        // sharing the default name resolve to the SAME underlying scheduler —
        // the second test then touches infrastructure built against the
        // first test's already-disposed ServiceProvider/LoggerFactory.
        services.AddQuartzJobScheduler(q => q.SchedulerName = $"test-{Guid.NewGuid()}");
        return services.BuildServiceProvider();
    }

    /// <summary>Finds the sole scheduled job's data map, regardless of its random group id.</summary>
    private static async Task<JobDataMap> GetSoleJobDataMapAsync(IScheduler scheduler, string jobName)
    {
        var keys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
        var key = keys.Should().ContainSingle(k => k.Name == jobName).Subject;
        var detail = await scheduler.GetJobDetail(key);
        detail.Should().NotBeNull();
        return detail!.JobDataMap;
    }

    [Fact]
    public async Task ScheduleAsync_CarriesAmbientTenantAndCorrelation_LikeEnqueueAsync()
    {
        var sp = BuildProvider();
        var tenant = sp.GetRequiredService<ICurrentTenant>();
        var correlation = sp.GetRequiredService<ICorrelationContext>();
        var scheduler = await sp.GetRequiredService<ISchedulerFactory>().GetScheduler();

        var tenantId = Guid.NewGuid();
        using var tenantScope = tenant.Change(new TenantInfo(tenantId, "acme"));
        using var correlationScope = correlation.BeginScope("corr-123");

        var jobScheduler = sp.GetRequiredService<IJobScheduler>();
        await jobScheduler.ScheduleAsync<TestJob, TestArgs>(new TestArgs("x"), TimeSpan.FromMinutes(5));

        var dataMap = await GetSoleJobDataMapAsync(scheduler, nameof(TestJob));
        dataMap.GetString("tenantId").Should().Be(tenantId.ToString("N"));
        dataMap.GetString("correlationId").Should().Be("corr-123");
    }

    [Fact]
    public async Task AddRecurring_CarriesAmbientTenantAndCorrelation_LikeEnqueueAsync()
    {
        var sp = BuildProvider();
        var tenant = sp.GetRequiredService<ICurrentTenant>();
        var correlation = sp.GetRequiredService<ICorrelationContext>();
        var scheduler = await sp.GetRequiredService<ISchedulerFactory>().GetScheduler();

        var tenantId = Guid.NewGuid();
        using var tenantScope = tenant.Change(new TenantInfo(tenantId, "acme"));
        using var correlationScope = correlation.BeginScope("corr-456");

        var jobScheduler = sp.GetRequiredService<IJobScheduler>();
        jobScheduler.AddRecurring<TestJob, TestArgs>("nightly-job", "0 0 0 * * ?", new TestArgs("x"));

        // AddRecurring is fire-and-forget (Task.Run) — poll briefly for the
        // deterministic (jobId, "recurring") key to land rather than assuming
        // synchronous completion.
        var key = new JobKey("nightly-job", "recurring");
        var exists = false;
        for (var i = 0; i < 50 && !exists; i++)
        {
            exists = await scheduler.CheckExists(key);
            if (!exists) await Task.Delay(20);
        }

        exists.Should().BeTrue("AddRecurring should have registered the job within the poll window");
        var detail = await scheduler.GetJobDetail(key);
        detail.Should().NotBeNull();
        detail!.JobDataMap.GetString("tenantId").Should().Be(tenantId.ToString("N"));
        detail.JobDataMap.GetString("correlationId").Should().Be("corr-456");
    }

    [Fact]
    public async Task EnqueueAsync_CarriesAmbientTenantAndCorrelation_Baseline()
    {
        // Baseline proving EnqueueAsync always carried this context — the bug
        // was ONLY in ScheduleAsync/AddRecurring diverging from it.
        var sp = BuildProvider();
        var tenant = sp.GetRequiredService<ICurrentTenant>();
        var correlation = sp.GetRequiredService<ICorrelationContext>();
        var scheduler = await sp.GetRequiredService<ISchedulerFactory>().GetScheduler();

        var tenantId = Guid.NewGuid();
        using var tenantScope = tenant.Change(new TenantInfo(tenantId, "acme"));
        using var correlationScope = correlation.BeginScope("corr-789");

        var jobScheduler = sp.GetRequiredService<IJobScheduler>();
        await jobScheduler.EnqueueAsync<TestJob, TestArgs>(new TestArgs("x"));

        var dataMap = await GetSoleJobDataMapAsync(scheduler, nameof(TestJob));
        dataMap.GetString("tenantId").Should().Be(tenantId.ToString("N"));
        dataMap.GetString("correlationId").Should().Be("corr-789");
    }

    // ── Test doubles ─────────────────────────────────────────────
    public sealed record TestArgs(string Value);

    public sealed class TestJob : IBackgroundJob<TestArgs>
    {
        public Task ExecuteAsync(TestArgs args, CancellationToken ct) => Task.CompletedTask;
    }
}
