namespace Modulus.Analytics.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// The data-quality checker on the shared harness: the warehouse rows of each company are profiled and judged against the dataset's rules.
public sealed partial class AnalyticsBackfillTests
{
    private static readonly AnalyticsDatasetDescriptor QualityDataset = new("quality", 1, "sales",
    [
        new("order_id", AnalyticsFieldType.Int64),
        new("coupon", AnalyticsFieldType.String, IsNullable: true, MaxNullRate: 0.25),
    ]);

    private Task WriteQualityRows(Guid tenant, params (long Id, string? Coupon, DateTimeOffset At)[] rows)
        => _sink.WriteAsync(
            QualityDataset,
            rows.Select(r => new AnalyticsRecord(Guid.NewGuid(), "quality", 1, tenant, r.At,
                new Dictionary<string, object?> { ["order_id"] = r.Id, ["coupon"] = r.Coupon })).ToList(),
            CancellationToken.None);

    [Fact]
    public async Task The_quality_check_reports_each_companys_broken_rules_and_leaves_a_clean_company_clean()
    {
        Build(analytics: a => a.AddDataset(QualityDataset));
        var now = _clock.GetUtcNow();
        await WriteQualityRows(_tenantA, (1, null, now), (2, null, now), (3, null, now), (4, "SAVE", now), (5, "LATER", now.AddHours(1)));
        await WriteQualityRows(_tenantB, (1, "A", now), (2, "B", now), (3, "C", now), (4, "D", now));

        var report = await InScope(sp => sp.GetRequiredService<AnalyticsQualityChecker>()
            .CheckAsync("quality", 1, [_tenantA, _tenantB], CancellationToken.None));

        report.Errors.Should().BeEmpty();
        var a = report.Results.Single(r => r.TenantId == _tenantA);
        a.RowCount.Should().Be(5);
        a.Findings.Select(f => f.Check).Should().BeEquivalentTo([AnalyticsQualityChecks.NullRateExceeded, AnalyticsQualityChecks.FutureOccurrence]);
        a.Findings.Single(f => f.Check == AnalyticsQualityChecks.NullRateExceeded).Observed.Should().BeApproximately(0.6, 1e-9, "three of five coupons are null");
        var b = report.Results.Single(r => r.TenantId == _tenantB);
        b.RowCount.Should().Be(4);
        b.Findings.Should().BeEmpty();
        report.IsClean.Should().BeFalse();
    }

    [Fact]
    public async Task A_dataset_that_is_not_registered_cannot_be_checked()
    {
        Build();

        var check = () => InScope(sp => sp.GetRequiredService<AnalyticsQualityChecker>().CheckAsync("quality", 1, [_tenantA], CancellationToken.None));

        await check.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'quality.v1' is not registered*");
    }
}
