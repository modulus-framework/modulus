using FluentAssertions;
using Modulus.Authorization;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Proves the effective-access report composes direct + delegated authority, reports them
/// distinctly (with on-behalf-of provenance), and surfaces SoD violations in the union —
/// the auditor/recertification snapshot (blueprint §5.14, §16).
/// </summary>
[Trait("Category", "Unit")]
public sealed class EffectiveAccessServiceTests
{
    private static readonly Guid Manager = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Deputy = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset T0 = new(2026, 07, 01, 0, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static IPermissionRegistry Registry(params string[] perms)
    {
        var registry = new PermissionRegistry();
        foreach (var p in perms)
            registry.Add(p, p, null);
        registry.Freeze();
        return registry;
    }

    [Fact]
    public void Report_ComposesDirectAndDelegated_Distinctly()
    {
        var grants = new InMemoryPermissionGrantStore()
            .GrantToRole("manager", "orders:approve")
            .GrantToUser(Deputy, "orders:read");
        var direct = new PermissionResolver(grants, Registry("orders:approve", "orders:read"));

        var delegations = new InMemoryDelegationStore();
        delegations.Delegate(Manager, ["manager"], Deputy, ["orders:approve"], T0, T0.AddDays(7));
        var delegationResolver = new DelegationResolver(delegations, direct, new FixedClock(T0.AddDays(1)));

        var service = new EffectiveAccessService(direct, delegationResolver, SodPolicy.Empty);

        var report = service.Report(new PrincipalGrantQuery(Deputy, []));

        report.DirectPermissions.Should().BeEquivalentTo(["orders:read"]);
        report.DelegatedPermissions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Permission = "orders:approve", OnBehalfOf = Manager });
        report.AllPermissions.Should().BeEquivalentTo(["orders:read", "orders:approve"]);
    }

    [Fact]
    public void Report_FlagsSodViolation_AcrossDirectAndDelegated()
    {
        // Deputy holds create directly and gets approve by delegation → a maker-checker breach.
        var grants = new InMemoryPermissionGrantStore()
            .GrantToUser(Deputy, "payments:create")
            .GrantToRole("manager", "payments:approve");
        var direct = new PermissionResolver(grants, Registry("payments:create", "payments:approve"));

        var delegations = new InMemoryDelegationStore();
        delegations.Delegate(Manager, ["manager"], Deputy, ["payments:approve"], T0, T0.AddDays(7));
        var delegationResolver = new DelegationResolver(delegations, direct, new FixedClock(T0.AddDays(1)));

        var sod = new SodPolicy([
            new SodConstraint("payments-maker-checker", ["payments:create", "payments:approve"])]);
        var service = new EffectiveAccessService(direct, delegationResolver, sod);

        var report = service.Report(new PrincipalGrantQuery(Deputy, []));

        report.SodViolations.Should().ContainSingle(
            "a delegation that combines with a direct grant to breach four-eyes must be visible to governance");
    }

    [Fact]
    public void Report_ForAnonymous_HasNoAccess()
    {
        var direct = new PermissionResolver(new InMemoryPermissionGrantStore(), Registry("orders:read"));
        var service = new EffectiveAccessService(direct, EmptyDelegationResolverProxy.Instance, SodPolicy.Empty);

        var report = service.Report(PrincipalGrantQuery.Anonymous);

        report.AllPermissions.Should().BeEmpty();
        report.DelegatedPermissions.Should().BeEmpty();
    }

    // The internal EmptyDelegationResolver is not visible to tests; a local no-op stands in.
    private sealed class EmptyDelegationResolverProxy : IDelegationResolver
    {
        public static readonly EmptyDelegationResolverProxy Instance = new();
        public IReadOnlyCollection<DelegatedPermission> DelegatedPermissions(Guid delegateUserId) => [];
    }
}
