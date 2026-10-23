using FluentAssertions;
using Modulus.Authorization;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Resources;
using Modulus.BackgroundJobs;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// P1b-GATE conformance for jobs and agents: the truth table of a sensitive job step queued by a person through an agent. The person's
/// rights come from the real permission resolver and grant store; an agent can only narrow them, and a denial always stands.
/// </summary>
[Trait("Category", "Unit")]
public sealed class JobAgentMatrixTests
{
    private const string Permission = "ops:wipe";
    private const string Ops = "Ops";
    private const string AgentRole = "integration:tablet";
    private static readonly Guid s_person = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // Mirrors UserJobInitiatorGuard: the person's roles are looked up and the resolver answers, with the agent's cap when there is one.
    private sealed class ResolverInitiatorGuard(PermissionResolver resolver) : IJobInitiatorGuard
    {
        public Task<bool> MayRunAsync(Guid initiatorId, CancellationToken ct) => Task.FromResult(true);

        public Task<bool> MayPerformAsync(Guid initiatorId, string permission, CancellationToken ct)
            => Task.FromResult(resolver.Resolve(new PrincipalGrantQuery(initiatorId, [Ops])).Contains(permission));

        public Task<bool> MayPerformAsAgentAsync(Guid initiatorId, string agentRole, string permission, CancellationToken ct)
            => Task.FromResult(resolver.Resolve(new PrincipalGrantQuery(initiatorId, [Ops], agentRole)).Contains(permission));
    }

    private static PermissionResolver Resolver(InMemoryPermissionGrantStore store)
    {
        var registry = new PermissionRegistry();
        registry.Add(Permission, Permission, null);
        registry.Freeze();
        return new PermissionResolver(store, registry);
    }

    private static async Task<bool> HoldsAsync(bool personHas, bool personDenied, bool agentHas, bool throughAgent)
    {
        var store = new InMemoryPermissionGrantStore();
        if (personHas)
            store.GrantToRole(Ops, Permission);
        if (personDenied)
            store.DenyToRole(Ops, Permission);
        if (agentHas)
            store.GrantToRole(AgentRole, Permission);

        var context = new JobContext
        {
            Actor = JobActor.ForPerson(s_person),
            AgentId = throughAgent ? AgentRole : null,
        };
        var guard = new JobStageGuard(context, new ResolverInitiatorGuard(Resolver(store)));
        return await guard.HoldsAsync(Permission);
    }

    // personHas, personDenied, agentHas, expected: the step holds when the person has it, nothing denies it, and the agent (if any) has it too.
    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, false, false)]
    public async Task A_step_through_an_agent_holds_only_when_the_person_and_the_agent_both_hold_it(
        bool personHas, bool personDenied, bool agentHas, bool expected)
    {
        (await HoldsAsync(personHas, personDenied, agentHas, throughAgent: true)).Should().Be(expected);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public async Task A_step_without_an_agent_is_checked_against_the_person_alone(bool personHas, bool personDenied, bool expected)
    {
        (await HoldsAsync(personHas, personDenied, agentHas: false, throughAgent: false)).Should().Be(expected);
    }

    [Fact]
    public async Task An_agent_holding_the_permission_cannot_widen_a_person_who_lacks_it_even_on_every_path()
    {
        (await HoldsAsync(personHas: false, personDenied: false, agentHas: true, throughAgent: true)).Should().BeFalse();
    }

    [Fact]
    public async Task A_system_job_with_no_person_behind_it_is_not_checked_as_before()
    {
        var guard = new JobStageGuard(new JobContext { Actor = JobActor.ForSystem("nightly-export") }, new ResolverInitiatorGuard(Resolver(new InMemoryPermissionGrantStore())));

        (await guard.HoldsAsync(Permission)).Should().BeTrue("a job with no person behind it is not checked, by design");
    }
}
