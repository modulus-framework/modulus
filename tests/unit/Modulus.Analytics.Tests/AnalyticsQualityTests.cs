namespace Modulus.Analytics.Tests;

using FluentAssertions;
using Xunit;

public sealed class AnalyticsQualityTests
{
    private static readonly AnalyticsDatasetDescriptor Dataset = new("orders", 1, "sales",
    [
        new("order_id", AnalyticsFieldType.Int64),
        new("coupon", AnalyticsFieldType.String, IsNullable: true, MaxNullRate: 0.25),
    ]);

    private static AnalyticsWarehouseProfile Profile(long rows, long nullCoupons = 0, long future = 0)
        => new(rows, future, new Dictionary<string, long> { ["coupon"] = nullCoupons });

    [Fact]
    public void A_null_rate_above_the_limit_is_a_finding_with_the_observed_rate()
    {
        var findings = AnalyticsQuality.Evaluate(Dataset, Profile(rows: 4, nullCoupons: 3));

        findings.Should().ContainSingle().Which.Should().Match<AnalyticsQualityFinding>(f =>
            f.Check == AnalyticsQualityChecks.NullRateExceeded && f.Field == "coupon" && f.Observed == 0.75 && f.Threshold == 0.25);
    }

    [Fact]
    public void A_null_rate_exactly_at_the_limit_and_a_field_without_a_rule_are_not_findings()
    {
        AnalyticsQuality.Evaluate(Dataset, Profile(rows: 4, nullCoupons: 1)).Should().BeEmpty("one null in four is exactly the 25% limit");
        AnalyticsQuality.Evaluate(Dataset, new AnalyticsWarehouseProfile(4, 0, new Dictionary<string, long>())).Should().BeEmpty("a missing count is zero nulls");
    }

    [Fact]
    public void Records_timestamped_in_the_future_are_a_dataset_wide_finding()
    {
        var findings = AnalyticsQuality.Evaluate(Dataset, Profile(rows: 10, nullCoupons: 0, future: 2));

        findings.Should().ContainSingle().Which.Should().Match<AnalyticsQualityFinding>(f =>
            f.Check == AnalyticsQualityChecks.FutureOccurrence && f.Field == null && f.Observed == 2);
    }

    [Fact]
    public void An_empty_profile_has_no_null_rate_to_judge()
    {
        AnalyticsQuality.Evaluate(Dataset, new AnalyticsWarehouseProfile(0, 0, new Dictionary<string, long>())).Should().BeEmpty();
    }

    [Fact]
    public void A_report_is_clean_only_when_nothing_failed_and_no_rule_broke()
    {
        var clean = new AnalyticsQualityReport([new AnalyticsQualityResult("orders", 1, Guid.NewGuid(), "memory", 4, [])], []);
        var broken = clean with
        {
            Results = [clean.Results[0] with { Findings = [new AnalyticsQualityFinding(AnalyticsQualityChecks.FutureOccurrence, null, 1, null, "x")] }],
        };

        clean.IsClean.Should().BeTrue();
        broken.IsClean.Should().BeFalse();
        (clean with { Errors = ["could not profile"] }).IsClean.Should().BeFalse();
    }

    [Fact]
    public void A_null_rate_limit_needs_a_nullable_field_and_a_rate_between_zero_and_one()
    {
        var required = new AnalyticsDatasetDescriptor("orders", 1, "sales", [new("order_id", AnalyticsFieldType.Int64, MaxNullRate: 0.1)]);
        var outOfRange = new AnalyticsDatasetDescriptor("orders", 1, "sales", [new("coupon", AnalyticsFieldType.String, IsNullable: true, MaxNullRate: 1.5)]);
        var valid = new AnalyticsDatasetDescriptor("orders", 1, "sales", [new("coupon", AnalyticsFieldType.String, IsNullable: true, MaxNullRate: 0)]);

        required.Validate().Should().ContainSingle().Which.Should().Contain("not nullable");
        outOfRange.Validate().Should().ContainSingle().Which.Should().Contain("between 0 and 1");
        valid.Validate().Should().BeEmpty();
    }
}
