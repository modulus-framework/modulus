using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.BackgroundJobs;
using Modulus.BackgroundJobs.Extensions;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// The read-only view a job has of its initiator: <see cref="JobContext.InitiatorId"/> and <see cref="IJobStageGuard.HoldsAsync"/>.
/// Handlers do not get the initiator as <see cref="ICurrentUser"/>; this view is what they can rely on.
/// </summary>
[Trait("Category", "Unit")]
public sealed class JobInitiatorViewTests
{
    private const string Permission = "ops:wipe";

    private sealed record TestArgs(int Id);

    private sealed class ViewJob(JobContext context, IJobStageGuard stage, ConcurrentBag<string> seen) : IBackgroundJob<TestArgs>
    {
        public async Task ExecuteAsync(TestArgs args, CancellationToken ct)
        {
            var holds = await stage.HoldsAsync(Permission, ct);
            seen.Add($"initiator={context.InitiatorId?.ToString("N") ?? "none"};holds={holds}");
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

    private sealed class PermissionGuard(bool holdsPermission) : IJobInitiatorGuard
    {
        public ConcurrentBag<Guid> Asked { get; } = [];

        public Task<bool> MayRunAsync(Guid initiatorId, CancellationToken ct) => Task.FromResult(true);

        public Task<bool> MayPerformAsync(Guid initiatorId, string permission, CancellationToken ct)
        {
            Asked.Add(initiatorId);
            return Task.FromResult(holdsPermission);
        }
    }

    private static (ChannelJobQueue Queue, ConcurrentBag<string> Seen, SettableUser User, PermissionGuard Guard) Build(bool holdsPermission)
    {
        var seen = new ConcurrentBag<string>();
        var user = new SettableUser();
        var guard = new PermissionGuard(holdsPermission);
        var services = new ServiceCollection();
        services.AddSingleton(seen);
        services.AddSingleton<ICurrentUser>(user);
        services.AddSingleton<IJobInitiatorGuard>(guard);
        services.AddBackgroundJobs();
        services.AddTransient<IBackgroundJob<TestArgs>, ViewJob>();
        return (new ChannelJobQueue(services.BuildServiceProvider(), NullLogger<ChannelJobQueue>.Instance), seen, user, guard);
    }

    [Fact]
    public async Task A_job_sees_its_initiator_and_that_the_initiator_holds_the_permission()
    {
        var (queue, seen, user, guard) = Build(holdsPermission: true);
        var initiator = Guid.NewGuid();
        await queue.StartAsync(CancellationToken.None);

        user.UserId = initiator;
        await queue.EnqueueAsync<ViewJob, TestArgs>(new TestArgs(1), CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);

        seen.Should().ContainSingle().Which.Should().Be($"initiator={initiator:N};holds=True");
        guard.Asked.Should().ContainSingle().Which.Should().Be(initiator);
    }

    [Fact]
    public async Task A_job_learns_without_an_exception_that_its_initiator_no_longer_holds_the_permission()
    {
        var (queue, seen, user, guard) = Build(holdsPermission: false);
        var initiator = Guid.NewGuid();
        await queue.StartAsync(CancellationToken.None);

        user.UserId = initiator;
        await queue.EnqueueAsync<ViewJob, TestArgs>(new TestArgs(2), CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);

        seen.Should().ContainSingle().Which.Should().Be($"initiator={initiator:N};holds=False");
        guard.Asked.Should().ContainSingle();
    }

    [Fact]
    public async Task A_job_with_no_person_behind_it_has_no_initiator_and_is_not_checked()
    {
        var (queue, seen, _, guard) = Build(holdsPermission: false);
        await queue.StartAsync(CancellationToken.None);

        await queue.EnqueueAsync<ViewJob, TestArgs>(new TestArgs(3), CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);

        seen.Should().ContainSingle().Which.Should().Be("initiator=none;holds=True");
        guard.Asked.Should().BeEmpty();
    }
}
