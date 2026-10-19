using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.BackgroundJobs;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// A6-2: every job runs as a named actor, a person who queued it or else the system named by its job type. The running job sees the
/// same name through <see cref="JobContext"/>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class JobActorTests
{
    private sealed record ActorArgs(int Id);

    private sealed class ReportingJob(ConcurrentBag<string?> seen, JobContext context) : IBackgroundJob<ActorArgs>
    {
        public Task ExecuteAsync(ActorArgs args, CancellationToken ct)
        {
            seen.Add(context.Actor);
            return Task.CompletedTask;
        }
    }

    private sealed class SettableUser : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => UserId is not null;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }

    private static (ChannelJobQueue Queue, ConcurrentBag<string?> Seen, SettableUser User) Build()
    {
        var seen = new ConcurrentBag<string?>();
        var user = new SettableUser();
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(user);
        services.AddScoped<JobContext>();
        services.AddTransient<IBackgroundJob<ActorArgs>>(sp => new ReportingJob(seen, sp.GetRequiredService<JobContext>()));
        return (new ChannelJobQueue(services.BuildServiceProvider(), NullLogger<ChannelJobQueue>.Instance), seen, user);
    }

    [Fact]
    public async Task A_job_queued_by_a_person_runs_as_that_person()
    {
        var (queue, seen, user) = Build();
        var person = Guid.NewGuid();
        await queue.StartAsync(CancellationToken.None);

        user.UserId = person;
        await queue.EnqueueAsync<ReportingJob, ActorArgs>(new ActorArgs(1), CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);

        seen.Should().ContainSingle().Which.Should().Be(JobActor.ForPerson(person));
    }

    [Fact]
    public async Task A_job_queued_without_a_person_runs_as_the_system_named_by_its_job_type()
    {
        var (queue, seen, _) = Build();
        await queue.StartAsync(CancellationToken.None);

        await queue.EnqueueAsync<ReportingJob, ActorArgs>(new ActorArgs(2), CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);

        seen.Should().ContainSingle().Which.Should().Be(JobActor.ForSystem(nameof(ReportingJob)));
    }

    [Fact]
    public void A_system_actor_needs_a_name()
    {
        var blank = () => JobActor.ForSystem(" ");

        blank.Should().Throw<ArgumentException>();
        JobActor.ForSystem("nightly-export").Should().Be("system:nightly-export");
    }
}
