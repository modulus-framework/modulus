using FluentAssertions;
using Modulus.Authorization;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// A9-1: an agent acting for a person is capped by its own role. The person's permissions, the delegated ones included, reach the
/// principal only where the agent role also holds them, and a deny on the agent role removes them.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AgentBoundPermissionTests
{
    private const string AgentRole = "agent:orders-bot";
    private static readonly Guid Person = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Other = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static IPermissionRegistry Registry(params string[] perms)
    {
        var registry = new PermissionRegistry();
        foreach (var p in perms)
            registry.Add(p, p, null);
        registry.Freeze();
        return registry;
    }

    private sealed class StubDelegations(params string[] permissions) : IDelegationResolver
    {
        public IReadOnlyCollection<DelegatedPermission> DelegatedPermissions(Guid delegateUserId)
            => [.. permissions.Select(p => new DelegatedPermission(p, Other, Guid.NewGuid()))];
    }

    // The person can read and approve; the agent role may only read.
    private static InMemoryPermissionGrantStore Grants() => new InMemoryPermissionGrantStore()
        .GrantToUser(Person, "orders:read")
        .GrantToUser(Person, "orders:approve")
        .GrantToRole(AgentRole, "orders:read");

    [Fact]
    public void The_person_acting_directly_keeps_all_of_their_permissions()
    {
        var resolver = new PermissionResolver(Grants(), Registry("orders:read", "orders:approve"));

        resolver.Resolve(new PrincipalGrantQuery(Person, [])).Should().BeEquivalentTo(["orders:read", "orders:approve"]);
    }

    [Fact]
    public void Through_an_agent_the_person_keeps_only_what_the_agent_role_holds()
    {
        var resolver = new PermissionResolver(Grants(), Registry("orders:read", "orders:approve"));

        resolver.Resolve(new PrincipalGrantQuery(Person, [], AgentRole)).Should().BeEquivalentTo(["orders:read"]);
    }

    [Fact]
    public void The_grants_overload_applies_the_same_cap()
    {
        var store = Grants();
        var resolver = new PermissionResolver(store, Registry("orders:read", "orders:approve"));
        var query = new PrincipalGrantQuery(Person, [], AgentRole);

        resolver.Resolve(query, store.GetGrants(query)).Should().BeEquivalentTo(["orders:read"]);
    }

    [Fact]
    public void A_deny_on_the_agent_role_removes_a_permission_the_person_holds()
    {
        var store = Grants().DenyToRole(AgentRole, "orders:read");
        var resolver = new PermissionResolver(store, Registry("orders:read", "orders:approve"));

        resolver.Resolve(new PrincipalGrantQuery(Person, [], AgentRole)).Should().BeEmpty();
    }

    [Fact]
    public void A_delegated_permission_does_not_escape_the_agent_cap()
    {
        var direct = new PermissionResolver(Grants(), Registry("orders:read", "orders:approve"));
        var resolver = new DelegationAwarePermissionResolver(direct, new StubDelegations("orders:approve"));

        resolver.Resolve(new PrincipalGrantQuery(Person, [], AgentRole)).Should().BeEquivalentTo(["orders:read"]);
    }

    [Fact]
    public void A_delegated_permission_reaches_a_person_acting_directly()
    {
        var direct = new PermissionResolver(Grants(), Registry("orders:read", "orders:approve"));
        var resolver = new DelegationAwarePermissionResolver(direct, new StubDelegations("orders:approve"));

        resolver.Resolve(new PrincipalGrantQuery(Person, [])).Should().Contain("orders:approve");
    }
}
