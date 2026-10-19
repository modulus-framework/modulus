using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.BackgroundJobs;
using Modulus.Core.Abstractions;
using Quartz;
using Xunit;

namespace Modulus.BackgroundJobs.Quartz.Tests;

/// <summary>
/// A6-1 on Quartz, the refusal path: a queued job whose initiator may no longer start access is skipped (not run, not retried) and the
/// refusal is audited. The scheduler is started, so the job really fires through <see cref="QuartzJobAdapter{TJob,TArgs}"/>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class QuartzRefusalTests
{
    private sealed class SignedInUser(Guid id) : ICurrentUser
    {
        public Guid? UserId => id;
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }

    private sealed class FixedGuard(bool allow) : IJobInitiatorGuard
    {
        public Task<bool> MayRunAsync(Guid initiatorId, CancellationToken ct) => Task.FromResult(allow);
    }

    private sealed class RecordingAudit : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];

        public void Record(SecurityAuditEvent auditEvent)
        {
            lock (Events)
                Events.Add(auditEvent);
        }
    }

    public sealed record RefusalArgs(string Value);

    public sealed class CountingJob(RunCount count) : IBackgroundJob<RefusalArgs>
    {
        public Task ExecuteAsync(RefusalArgs args, CancellationToken ct)
        {
            Interlocked.Increment(ref count.Value);
            return Task.CompletedTask;
        }
    }

    public sealed class RunCount
    {
        public int Value;
    }

    private static (ServiceProvider Provider, RunCount Runs, RecordingAudit Audit) Build(bool allow)
    {
        var runs = new RunCount();
        var audit = new RecordingAudit();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(runs);
        services.AddSingleton<ICurrentUser>(new SignedInUser(Guid.NewGuid()));
        services.AddSingleton<ISecurityAuditLog>(audit);
        services.AddScoped<IJobInitiatorGuard>(_ => new FixedGuard(allow));
        services.AddScoped<CountingJob>();
        services.AddScoped<IBackgroundJob<RefusalArgs>>(sp => sp.GetRequiredService<CountingJob>());
        services.AddQuartzJobScheduler(q => q.SchedulerName = $"refusal-{Guid.NewGuid()}");
        return (services.BuildServiceProvider(), runs, audit);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(50);
    }

    [Fact]
    public async Task A_refused_initiator_skips_the_job_and_the_refusal_is_audited()
    {
        var (provider, runs, audit) = Build(allow: false);
        await using var _ = provider;
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();

        await provider.GetRequiredService<IJobScheduler>().EnqueueAsync<CountingJob, RefusalArgs>(new RefusalArgs("x"));
        await WaitForAsync(() => audit.Events.Count > 0);
        await scheduler.Shutdown(waitForJobsToComplete: true);

        runs.Value.Should().Be(0, "the initiator may no longer start access");
        audit.Events.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Category = SecurityAuditCategories.Authorization,
            Action = "job.refused",
            Outcome = SecurityAuditOutcomes.Denied,
            Target = nameof(CountingJob),
        }, o => o.ExcludingMissingMembers());
    }

    [Fact]
    public async Task An_allowed_initiator_runs_the_job_control_for_the_refusal_test()
    {
        var (provider, runs, audit) = Build(allow: true);
        await using var _ = provider;
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();

        await provider.GetRequiredService<IJobScheduler>().EnqueueAsync<CountingJob, RefusalArgs>(new RefusalArgs("y"));
        await WaitForAsync(() => runs.Value > 0);
        await scheduler.Shutdown(waitForJobsToComplete: true);

        runs.Value.Should().Be(1);
        audit.Events.Should().BeEmpty();
    }
}
