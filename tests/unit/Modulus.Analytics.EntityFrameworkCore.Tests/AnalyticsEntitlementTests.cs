namespace Modulus.Analytics.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Features;
using Modulus.Core.Abstractions.Entities;
using Xunit;

// Plan gating on the shared harness: a dataset that names a required feature, and a feature resolver the test controls.
public sealed partial class AnalyticsBackfillTests
{
    private const string Feature = "analytics.advanced";

    private static readonly AnalyticsDatasetDescriptor GatedDataset = new("gated", 1, "sales",
    [
        new("order_id", AnalyticsFieldType.Int64),
        new("total", AnalyticsFieldType.Decimal),
        new("email", AnalyticsFieldType.String, Classification: FieldClassification.Confidential),
    ],
    RequiredFeature: Feature);

    private sealed class FakeFeatures : IFeatureEntitlementResolver
    {
        public HashSet<Guid> Entitled { get; } = [];

        public bool IsEnabled(Guid tenantId, string feature) => feature == Feature && Entitled.Contains(tenantId);

        public IReadOnlySet<string> EnabledFeatures(Guid tenantId) => Entitled.Contains(tenantId) ? new HashSet<string> { Feature } : [];
    }

    private ServiceProvider BuildGated(FakeFeatures features)
        => Build(
            extra: s => s.AddSingleton<IFeatureEntitlementResolver>(features),
            analytics: a => a.AddDataset(GatedDataset).AddEntitySource<OrdersDb, Order, long>("gated", 1, o => o.Id, o => new AnalyticsEntityRow(
                AnalyticsRecordIds.Deterministic("gated", o.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero),
                new Dictionary<string, object?> { ["order_id"] = o.Id, ["total"] = o.Total, ["email"] = o.Email })));

    private Task<AnalyticsBackfillStartResult> StartGated(params Guid[] tenants)
        => InScope(sp => sp.GetRequiredService<AnalyticsBackfillManager>().StartAsync("gated", 1, tenants, false, "tester", CancellationToken.None));

    [Fact]
    public void The_feature_entitlement_checks_the_tenants_plan_only_for_a_dataset_that_names_a_feature()
    {
        var features = new FakeFeatures();
        features.Entitled.Add(_tenantA);
        var entitlement = new FeatureAnalyticsEntitlement(new ServiceCollection().AddSingleton<IFeatureEntitlementResolver>(features).BuildServiceProvider());

        entitlement.IsEntitled(_tenantA, GatedDataset).Should().BeTrue();
        entitlement.IsEntitled(_tenantB, GatedDataset).Should().BeFalse();
        entitlement.IsEntitled(_tenantB, GatedDataset with { RequiredFeature = null }).Should().BeTrue("a dataset with no required feature is open to everyone");
    }

    [Fact]
    public void An_app_without_an_entitlement_store_is_open_to_every_tenant()
    {
        var entitlement = new FeatureAnalyticsEntitlement(new ServiceCollection().BuildServiceProvider());

        entitlement.IsEntitled(_tenantA, GatedDataset).Should().BeTrue();
    }

    [Fact]
    public async Task Starting_a_run_skips_tenants_whose_plan_lacks_the_feature_and_says_so()
    {
        var features = new FakeFeatures();
        features.Entitled.Add(_tenantA);
        BuildGated(features);

        var result = await StartGated(_tenantA, _tenantB);

        result.Should().Be(new AnalyticsBackfillStartResult(1, 0, 0, 1));
        (await Runs()).Should().ContainSingle().Which.TenantId.Should().Be(_tenantA);
    }

    [Fact]
    public async Task A_dataset_without_a_required_feature_is_unaffected_by_the_entitlement_store()
    {
        BuildGated(new FakeFeatures());
        await SeedOrdersAsync(_tenantA, 2);

        var result = await Start([_tenantA, _tenantB]);

        result.Should().Be(new AnalyticsBackfillStartResult(2, 0, 0, 0));
    }

    [Fact]
    public async Task A_run_for_a_tenant_whose_entitlement_is_withdrawn_fails_visibly_and_reads_nothing()
    {
        var features = new FakeFeatures();
        features.Entitled.Add(_tenantA);
        BuildGated(features);
        await SeedOrdersAsync(_tenantA, 3);
        await StartGated(_tenantA);
        features.Entitled.Clear();

        await Cycle();

        var run = (await Runs()).Single();
        run.Status.Should().Be(AnalyticsBackfillStatus.Failed);
        run.LastError.Should().Contain("not entitled").And.Contain(Feature);
        (await Queued()).Should().BeEmpty();

        features.Entitled.Add(_tenantA);
        var restarted = await InScope(sp => sp.GetRequiredService<AnalyticsBackfillManager>().StartAsync("gated", 1, [_tenantA], true, "tester", CancellationToken.None));
        restarted.Restarted.Should().Be(1);
        await DrainRunsAsync();
        (await Queued()).Should().HaveCount(3, "after the plan changed and the run was restarted, it reads normally");
    }

    [Fact]
    public async Task Reconciliation_does_not_compare_a_tenant_that_is_not_entitled()
    {
        var features = new FakeFeatures();
        features.Entitled.Add(_tenantA);
        BuildGated(features);
        await SeedOrdersAsync(_tenantA, 2);
        await SeedOrdersAsync(_tenantB, 2, firstId: 100);
        await StartGated(_tenantA);
        await DrainRunsAsync();
        await DrainDeliveriesAsync();

        var report = await InScope(sp => sp.GetRequiredService<AnalyticsReconciler>().ReconcileAsync("gated", 1, [_tenantA, _tenantB], CancellationToken.None));

        report.Results.Should().ContainSingle().Which.TenantId.Should().Be(_tenantA);
        report.Errors.Should().ContainSingle().Which.Should().Contain(_tenantB.ToString()).And.Contain("not entitled");
        report.IsClean.Should().BeFalse();
    }
}
