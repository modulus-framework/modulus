using FluentAssertions;
using Modulus.Authorization.Features;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Proves the pure feature-entitlement resolver: plan bundles grant features, tenant
/// overrides win over the plan in both directions (add-on on, jurisdictional block off),
/// and an untenanted/unplanned feature is fail-closed (blueprint §5.11, §14).
/// </summary>
[Trait("Category", "Unit")]
public sealed class FeatureEntitlementResolverTests
{
    private static readonly Guid Free = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Enterprise = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Blocked = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static FeatureEntitlementResolver Resolver()
    {
        var store = new InMemoryFeatureEntitlementStore()
            .DefinePlan("free", "core")
            .DefinePlan("enterprise", "core", "analytics.advanced", "einvoicing")
            .AssignPlan(Free, "free")
            .AssignPlan(Enterprise, "enterprise")
            .AssignPlan(Blocked, "enterprise")
            .Enable(Free, "analytics.advanced")   // add-on purchased outside the free plan
            .Disable(Blocked, "einvoicing");       // jurisdictional block despite the plan
        return new FeatureEntitlementResolver(store);
    }

    [Fact]
    public void PlanBundle_GrantsItsFeatures()
    {
        var resolver = Resolver();

        resolver.IsEnabled(Enterprise, "analytics.advanced").Should().BeTrue();
        resolver.IsEnabled(Enterprise, "core").Should().BeTrue();
    }

    [Fact]
    public void FeatureOutsideThePlan_IsNotAvailable_FailClosed()
    {
        Resolver().IsEnabled(Free, "einvoicing")
            .Should().BeFalse("the free plan does not bundle e-invoicing and no override enables it");
    }

    [Fact]
    public void EnableOverride_GrantsAnAddOn_OutsideThePlan()
    {
        Resolver().IsEnabled(Free, "analytics.advanced")
            .Should().BeTrue("the tenant purchased advanced analytics as an add-on");
    }

    [Fact]
    public void DisableOverride_RemovesAPlanFeature()
    {
        Resolver().IsEnabled(Blocked, "einvoicing")
            .Should().BeFalse("e-invoicing is blocked for this tenant despite the enterprise plan");
    }

    [Fact]
    public void UnknownTenant_HasNoFeatures_FailClosed()
    {
        Resolver().IsEnabled(Guid.NewGuid(), "core").Should().BeFalse();
    }

    [Fact]
    public void UnknownFeature_IsNotAvailable()
    {
        Resolver().IsEnabled(Enterprise, "does.not.exist").Should().BeFalse();
    }

    [Fact]
    public void EnabledFeatures_FoldsPlanAndOverrides_ForMenuBuilding()
    {
        var resolver = Resolver();

        resolver.EnabledFeatures(Free)
            .Should().BeEquivalentTo(["core", "analytics.advanced"]);

        resolver.EnabledFeatures(Blocked)
            .Should().BeEquivalentTo(["core", "analytics.advanced"],
                "the enterprise bundle minus the blocked e-invoicing feature");
    }

    [Fact]
    public void RuntimeMutation_TakesEffectImmediately()
    {
        var store = new InMemoryFeatureEntitlementStore()
            .DefinePlan("free", "core")
            .AssignPlan(Free, "free");
        var resolver = new FeatureEntitlementResolver(store);

        resolver.IsEnabled(Free, "analytics.advanced").Should().BeFalse();

        store.Enable(Free, "analytics.advanced"); // e.g. a billing upgrade event

        resolver.IsEnabled(Free, "analytics.advanced").Should().BeTrue();
    }
}
