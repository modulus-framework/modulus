using System.Collections.Concurrent;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.BackgroundJobs;
using Modulus.BackgroundJobs.Extensions;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// A9-1 for background jobs: a job queued through an agent carries the agent role, and its permission checks are capped by the
/// agent's rights. A job queued by a person without an agent is checked as before.
/// </summary>
[Trait("Category", "Unit")]
public sealed class JobAgentCapTests
{
    private const string Permission = "ops:wipe";
    private const string AgentRole = "integration:tablet";

    private sealed record TestArgs(int Id);

    private sealed class ProbeJob(JobContext context, IJobStageGuard stage, ConcurrentBag<string> seen) : IBackgroundJob<TestArgs>
    {
        public async Task ExecuteAsync(TestArgs args, CancellationToken ct)
        {
            var holds = await stage.HoldsAsync(Permission, ct);
            seen.Add($"holds={holds};agent={context.AgentId ?? "none"}");
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

    private sealed class CapGuard(bool holds) : IJobInitiatorGuard
    {
        public ConcurrentBag<string> Asked { get; } = [];

        public Task<bool> MayRunAsync(Guid initiatorId, CancellationToken ct) => Task.FromResult(true);

        public Task<bool> MayPerformAsync(Guid initiatorId, string permission, CancellationToken ct)
        {
            Asked.Add($"person:{permission}");
            return Task.FromResult(holds);
        }

        public Task<bool> MayPerformAsAgentAsync(Guid initiatorId, string agentRole, string permission, CancellationToken ct)
        {
            Asked.Add($"agent:{agentRole}:{permission}");
            return Task.FromResult(holds);
        }
    }

    private sealed class Accessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private static HttpContext Request(string? act)
    {
        var http = new DefaultHttpContext();
        var claims = act is null ? Array.Empty<Claim>() : new[] { new Claim("act", act) };
        http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return http;
    }

    private static (ChannelJobQueue Queue, ConcurrentBag<string> Seen, CapGuard Guard, Accessor Http, SettableUser User) Build(bool holds)
    {
        var seen = new ConcurrentBag<string>();
        var guard = new CapGuard(holds);
        var http = new Accessor(null);
        var user = new SettableUser();
        var services = new ServiceCollection();
        services.AddSingleton(seen);
        services.AddSingleton<ICurrentUser>(user);
        services.AddSingleton<IHttpContextAccessor>(http);
        services.AddSingleton<IJobInitiatorGuard>(guard);
        services.AddBackgroundJobs();
        services.AddTransient<IBackgroundJob<TestArgs>, ProbeJob>();
        return (new ChannelJobQueue(services.BuildServiceProvider(), NullLogger<ChannelJobQueue>.Instance), seen, guard, http, user);
    }

    [Fact]
    public async Task A_job_queued_through_an_agent_is_capped_by_the_agent_role()
    {
        var (queue, seen, guard, http, user) = Build(holds: true);
        await queue.StartAsync(CancellationToken.None);
        user.UserId = Guid.NewGuid();
        http.HttpContext = Request(AgentRole);

        await queue.EnqueueAsync<ProbeJob, TestArgs>(new TestArgs(1), CancellationToken.None);
        http.HttpContext = null;
        await queue.StopAsync(CancellationToken.None);

        seen.Should().ContainSingle().Which.Should().Be($"holds=True;agent={AgentRole}");
        guard.Asked.Should().ContainSingle().Which.Should().Be($"agent:{AgentRole}:{Permission}");
    }

    [Fact]
    public async Task A_job_queued_through_an_agent_is_refused_when_the_agent_role_lacks_the_permission()
    {
        var (queue, seen, guard, http, user) = Build(holds: false);
        await queue.StartAsync(CancellationToken.None);
        user.UserId = Guid.NewGuid();
        http.HttpContext = Request(AgentRole);

        await queue.EnqueueAsync<ProbeJob, TestArgs>(new TestArgs(2), CancellationToken.None);
        http.HttpContext = null;
        await queue.StopAsync(CancellationToken.None);

        seen.Should().ContainSingle().Which.Should().Be($"holds=False;agent={AgentRole}");
        guard.Asked.Should().ContainSingle().Which.Should().StartWith("agent:");
    }

    [Fact]
    public async Task A_job_queued_by_a_person_without_an_agent_is_checked_as_before()
    {
        var (queue, seen, guard, http, user) = Build(holds: true);
        await queue.StartAsync(CancellationToken.None);
        user.UserId = Guid.NewGuid();
        http.HttpContext = Request(act: null);

        await queue.EnqueueAsync<ProbeJob, TestArgs>(new TestArgs(3), CancellationToken.None);
        http.HttpContext = null;
        await queue.StopAsync(CancellationToken.None);

        seen.Should().ContainSingle().Which.Should().Be("holds=True;agent=none");
        guard.Asked.Should().ContainSingle().Which.Should().Be($"person:{Permission}");
    }
}
