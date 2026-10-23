using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.BackgroundJobs;
using Modulus.BackgroundJobs.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// A6-1 (per-stage): a running job re-checks, before a sensitive step, that the person who queued it still holds the permission.
/// A job with no person behind it is not checked. The check reads the job's own actor, so it cannot be fooled by the current caller.
/// </summary>
[Trait("Category", "Unit")]
public sealed class JobStageTests
{
    private const string Permission = "ops:wipe";

    private sealed record TestArgs(int Id);

    private sealed class StageJob(IJobStageGuard stage, ConcurrentBag<string> outcomes) : IBackgroundJob<TestArgs>
    {
        public async Task ExecuteAsync(TestArgs args, CancellationToken ct)
        {
            try
            {
                await stage.EnsureAllowedAsync(Permission, ct);
                outcomes.Add("allowed");
            }
            catch (ForbiddenException)
            {
                outcomes.Add("refused");
            }
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
        public ConcurrentBag<(Guid Initiator, string Permission)> Asked { get; } = [];

        public Task<bool> MayRunAsync(Guid initiatorId, CancellationToken ct) => Task.FromResult(true);

        public Task<bool> MayPerformAsync(Guid initiatorId, string permission, CancellationToken ct)
        {
            Asked.Add((initiatorId, permission));
            return Task.FromResult(holdsPermission);
        }
    }

    private static (ChannelJobQueue Queue, ConcurrentBag<string> Outcomes, SettableUser User, PermissionGuard Guard) Build(bool holdsPermission)
    {
        var outcomes = new ConcurrentBag<string>();
        var user = new SettableUser();
        var guard = new PermissionGuard(holdsPermission);
        var services = new ServiceCollection();
        services.AddSingleton(outcomes);
        services.AddSingleton<ICurrentUser>(user);
        services.AddSingleton<IJobInitiatorGuard>(guard);
        services.AddBackgroundJobs(); // registers JobContext and IJobStageGuard; the guard above is registered first, so it stays
        services.AddTransient<IBackgroundJob<TestArgs>, StageJob>();
        return (new ChannelJobQueue(services.BuildServiceProvider(), NullLogger<ChannelJobQueue>.Instance), outcomes, user, guard);
    }

    [Fact]
    public async Task A_step_runs_when_the_initiator_still_holds_the_permission()
    {
        var (queue, outcomes, user, guard) = Build(holdsPermission: true);
        var initiator = Guid.NewGuid();
        await queue.StartAsync(CancellationToken.None);

        user.UserId = initiator;
        await queue.EnqueueAsync<StageJob, TestArgs>(new TestArgs(1), CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);

        outcomes.Should().ContainSingle().Which.Should().Be("allowed");
        guard.Asked.Should().ContainSingle().Which.Should().Be((initiator, Permission));
    }

    [Fact]
    public async Task A_step_is_refused_when_the_initiator_has_lost_the_permission_since_queueing()
    {
        var (queue, outcomes, user, guard) = Build(holdsPermission: false);
        var initiator = Guid.NewGuid();
        await queue.StartAsync(CancellationToken.None);

        user.UserId = initiator;
        await queue.EnqueueAsync<StageJob, TestArgs>(new TestArgs(2), CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);

        outcomes.Should().ContainSingle().Which.Should().Be("refused");
        guard.Asked.Should().ContainSingle().Which.Should().Be((initiator, Permission));
    }

    [Fact]
    public async Task A_step_of_a_job_with_no_person_behind_it_is_not_checked()
    {
        var (queue, outcomes, _, guard) = Build(holdsPermission: false);
        await queue.StartAsync(CancellationToken.None);

        await queue.EnqueueAsync<StageJob, TestArgs>(new TestArgs(3), CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);

        outcomes.Should().ContainSingle().Which.Should().Be("allowed");
        guard.Asked.Should().BeEmpty();
    }
}
