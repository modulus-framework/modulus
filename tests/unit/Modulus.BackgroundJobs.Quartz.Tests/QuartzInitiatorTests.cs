using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.BackgroundJobs;
using Modulus.Core.Abstractions;
using Quartz;
using Quartz.Impl.Matchers;
using Xunit;

namespace Modulus.BackgroundJobs.Quartz.Tests;

/// <summary>
/// A6-1 on Quartz: a job queued by a signed-in person carries that person's id in its data map (the adapter checks it before the job
/// runs); a recurring job runs as the system and carries none.
/// </summary>
[Trait("Category", "Unit")]
public sealed class QuartzInitiatorTests
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

    public sealed record TestArgs(string Value);

    public sealed class TestJob : IBackgroundJob<TestArgs>
    {
        public Task ExecuteAsync(TestArgs args, CancellationToken ct) => Task.CompletedTask;
    }

    private static ServiceProvider BuildProvider(Guid userId)
    {
        var services = new ServiceCollection();
        services.AddSilentLogging();
        services.AddSingleton<ICurrentUser>(new SignedInUser(userId));
        services.AddQuartzJobScheduler(q => q.SchedulerName = $"test-{Guid.NewGuid()}");
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task EnqueueAsync_carries_the_signed_in_person_as_the_initiator()
    {
        var userId = Guid.NewGuid();
        var sp = BuildProvider(userId);
        var scheduler = await sp.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await sp.GetRequiredService<IJobScheduler>().EnqueueAsync<TestJob, TestArgs>(new TestArgs("x"));

        var keys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
        var detail = await scheduler.GetJobDetail(keys.Single(k => k.Name == nameof(TestJob)));
        detail!.JobDataMap.GetString("initiatorId").Should().Be(userId.ToString("N"));
        detail.JobDataMap.GetString("actor").Should().Be(JobActor.ForPerson(userId));
    }

    [Fact]
    public async Task AddRecurring_carries_no_initiator_because_it_runs_as_the_system()
    {
        var sp = BuildProvider(Guid.NewGuid());
        var scheduler = await sp.GetRequiredService<ISchedulerFactory>().GetScheduler();

        sp.GetRequiredService<IJobScheduler>().AddRecurring<TestJob, TestArgs>("nightly-initiator", "0 0 0 * * ?", new TestArgs("x"));

        var key = new JobKey("nightly-initiator", "recurring");
        var exists = false;
        for (var i = 0; i < 50 && !exists; i++)
        {
            exists = await scheduler.CheckExists(key);
            if (!exists) await Task.Delay(20);
        }

        exists.Should().BeTrue();
        var detail = await scheduler.GetJobDetail(key);
        detail!.JobDataMap.ContainsKey("initiatorId").Should().BeFalse();
        detail.JobDataMap.GetString("actor").Should().Be(JobActor.ForSystem("nightly-initiator"));
    }
}
