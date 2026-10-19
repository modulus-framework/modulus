using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.BackgroundJobs;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// A6-1: a job queued by a signed-in person runs only while <see cref="IJobInitiatorGuard"/> still lets that person start access.
/// A refused job is skipped (not retried), a job queued without a person is never checked, and the initiator is captured at enqueue.
/// </summary>
[Trait("Category", "Unit")]
public sealed class JobInitiatorTests
{
    private sealed record TestArgs(int Id);

    private sealed class RecordingJob(ConcurrentBag<int> ran) : IBackgroundJob<TestArgs>
    {
        public Task ExecuteAsync(TestArgs args, CancellationToken ct)
        {
            ran.Add(args.Id);
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

    private sealed class FakeGuard(bool allow) : IJobInitiatorGuard
    {
        public ConcurrentBag<Guid> Asked { get; } = [];

        public Task<bool> MayRunAsync(Guid initiatorId, CancellationToken ct)
        {
            Asked.Add(initiatorId);
            return Task.FromResult(allow);
        }
    }

    private static (ChannelJobQueue Queue, ConcurrentBag<int> Ran, SettableUser User, FakeGuard Guard) Build(bool allow)
    {
        var ran = new ConcurrentBag<int>();
        var user = new SettableUser();
        var guard = new FakeGuard(allow);
        var services = new ServiceCollection();
        services.AddSingleton(ran);
        services.AddSingleton<ICurrentUser>(user);
        services.AddSingleton<IJobInitiatorGuard>(guard);
        services.AddTransient<IBackgroundJob<TestArgs>>(sp => new RecordingJob(sp.GetRequiredService<ConcurrentBag<int>>()));
        return (new ChannelJobQueue(services.BuildServiceProvider(), NullLogger<ChannelJobQueue>.Instance), ran, user, guard);
    }

    [Fact]
    public async Task A_job_whose_initiator_may_no_longer_start_access_is_skipped()
    {
        var (queue, ran, user, guard) = Build(allow: false);
        var initiator = Guid.NewGuid();
        await queue.StartAsync(CancellationToken.None);

        user.UserId = initiator;
        await queue.EnqueueAsync<RecordingJob, TestArgs>(new TestArgs(1), CancellationToken.None);
        user.UserId = null; // captured at enqueue, not read again on the worker
        await queue.StopAsync(CancellationToken.None);

        ran.Should().BeEmpty("the initiator is refused, so the job must not run");
        guard.Asked.Should().ContainSingle().Which.Should().Be(initiator);
    }

    [Fact]
    public async Task A_job_whose_initiator_may_still_start_access_runs()
    {
        var (queue, ran, user, guard) = Build(allow: true);
        var initiator = Guid.NewGuid();
        await queue.StartAsync(CancellationToken.None);

        user.UserId = initiator;
        await queue.EnqueueAsync<RecordingJob, TestArgs>(new TestArgs(2), CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);

        ran.Should().ContainSingle().Which.Should().Be(2);
        guard.Asked.Should().ContainSingle().Which.Should().Be(initiator);
    }

    [Fact]
    public async Task A_job_queued_without_a_signed_in_person_is_not_checked()
    {
        var (queue, ran, _, guard) = Build(allow: false);
        await queue.StartAsync(CancellationToken.None);

        await queue.EnqueueAsync<RecordingJob, TestArgs>(new TestArgs(3), CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);

        ran.Should().ContainSingle().Which.Should().Be(3);
        guard.Asked.Should().BeEmpty();
    }

    private sealed class RecordingAudit : ISecurityAuditLog
    {
        public ConcurrentBag<SecurityAuditEvent> Events { get; } = [];

        public void Record(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }

    [Fact]
    public async Task A_refused_job_is_audited_with_the_person_who_queued_it()
    {
        var ran = new ConcurrentBag<int>();
        var user = new SettableUser();
        var audit = new RecordingAudit();
        var services = new ServiceCollection();
        services.AddSingleton(ran);
        services.AddSingleton<ICurrentUser>(user);
        services.AddSingleton<IJobInitiatorGuard>(new FakeGuard(allow: false));
        services.AddSingleton<ISecurityAuditLog>(audit);
        services.AddTransient<IBackgroundJob<TestArgs>>(sp => new RecordingJob(sp.GetRequiredService<ConcurrentBag<int>>()));
        var queue = new ChannelJobQueue(services.BuildServiceProvider(), NullLogger<ChannelJobQueue>.Instance);
        var initiator = Guid.NewGuid();
        await queue.StartAsync(CancellationToken.None);

        user.UserId = initiator;
        await queue.EnqueueAsync<RecordingJob, TestArgs>(new TestArgs(4), CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);

        ran.Should().BeEmpty();
        audit.Events.Should().ContainSingle().Which.Should().BeEquivalentTo(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Authorization,
            Action = "job.refused",
            Outcome = SecurityAuditOutcomes.Denied,
            Actor = JobActor.ForPerson(initiator),
            Target = nameof(RecordingJob),
        });
    }
}
