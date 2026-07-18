using FluentAssertions;
using Modulus.Authorization.Features;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Proves the <see cref="FeatureGate"/> edge bridge resolves availability for the tenant
/// in scope and mirrors tenant-isolation semantics: the host sees every feature, a
/// resolved tenant defers to its entitlements, and a request with no tenant resolved is
/// fail-closed (blueprint §5.11, §14).
/// </summary>
[Trait("Category", "Unit")]
public sealed class FeatureGateTests
{
    private static readonly Guid Tenant = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static FeatureEntitlementResolver Resolver()
        => new(new InMemoryFeatureEntitlementStore()
            .DefinePlan("enterprise", "analytics.advanced")
            .AssignPlan(Tenant, "enterprise"));

    [Fact]
    public void ResolvedTenant_GetsItsPlanFeatures()
    {
        var gate = new FeatureGate(new StubTenant(Tenant), Resolver());

        gate.IsEnabled("analytics.advanced").Should().BeTrue();
        gate.IsEnabled("einvoicing").Should().BeFalse("no plan or override grants it");
    }

    [Fact]
    public void HostContext_SeesEveryFeature()
    {
        var gate = new FeatureGate(new StubTenant(tenantId: null, isHost: true), Resolver());

        gate.IsEnabled("anything").Should().BeTrue("host is the deliberate all-features scope");
    }

    [Fact]
    public void NoTenantResolved_IsFailClosed()
    {
        // Multi-tenancy on but nothing established (missing header, background job).
        var gate = new FeatureGate(new StubTenant(tenantId: null, isHost: false), Resolver());

        gate.IsEnabled("analytics.advanced")
            .Should().BeFalse("a request with no tenant sees no gated feature, not every tenant's set");
    }

    private sealed class StubTenant(Guid? tenantId, bool isHost = false) : ICurrentTenant
    {
        public Guid? TenantId => tenantId;
        public string? TenantSlug => null;
        public bool IsAvailable => tenantId is not null;
        public bool IsHost => isHost;
        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }
}
