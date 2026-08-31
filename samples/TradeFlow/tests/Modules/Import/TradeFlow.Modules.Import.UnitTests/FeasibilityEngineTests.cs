using FluentAssertions;
using TradeFlow.Shared.Application.Abstractions.Import;
using TradeFlow.Shared.Infrastructure.Import;

namespace TradeFlow.Modules.Import.UnitTests;

public class HeuristicFeasibilityEngineTests
{
    private readonly HeuristicFeasibilityEngine _engine = new();

    [Fact]
    public void Evaluate_IneligibleVendor_ReturnsNotFeasibleWithZeroScore()
    {
        var input = new FeasibilityInput(
            VendorEligible: false,
            VendorScorecardAverage: 90m,
            BudgetHeadroomRatio: 1.5m,
            EstimatedDutyExposureRatio: 0.05m,
            VendorLeadTimeDays: 20,
            LcFacilityAvailable: true,
            PoValueBdt: 1_000_000m);

        var result = _engine.Evaluate(input);

        result.Verdict.Should().Be(FeasibilityVerdict.NotFeasible);
        result.Score.Should().Be(0m);
        result.Reasons.Should().Contain(r => r.Contains("not eligible"));
    }

    [Fact]
    public void Evaluate_AllSignalsGood_ReturnsFeasible()
    {
        var input = new FeasibilityInput(
            VendorEligible: true,
            VendorScorecardAverage: 85m,
            BudgetHeadroomRatio: 1.3m,
            EstimatedDutyExposureRatio: 0.10m,
            VendorLeadTimeDays: 25,
            LcFacilityAvailable: true,
            PoValueBdt: 500_000m,
            MarginPct: 0.25m,
            CostCompetitivenessIndex: 0.90m,
            SupplierRiskScore: 20m,
            NeedByDaysFromNow: 60,
            EstimatedArrivalDays: 45,
            HistoricalForecastAccuracy: 0.85m,
            PlanAligned: true,
            BudgetApproved: true);

        var result = _engine.Evaluate(input);

        result.Verdict.Should().Be(FeasibilityVerdict.Feasible);
        result.Score.Should().BeGreaterThanOrEqualTo(70m);
        result.Factors.Should().HaveCount(6);
        result.NormalizedWeights.Should().ContainKey("MarginAdequacy");
    }

    [Fact]
    public void Evaluate_HighRiskVendor_LowersScore()
    {
        var baseInput = new FeasibilityInput(
            VendorEligible: true,
            VendorScorecardAverage: 85m,
            BudgetHeadroomRatio: 1.3m,
            EstimatedDutyExposureRatio: 0.10m,
            VendorLeadTimeDays: 25,
            LcFacilityAvailable: true,
            PoValueBdt: 500_000m,
            MarginPct: 0.20m,
            CostCompetitivenessIndex: 0.95m,
            SupplierRiskScore: 20m,
            NeedByDaysFromNow: 60,
            EstimatedArrivalDays: 45,
            HistoricalForecastAccuracy: 0.85m,
            PlanAligned: true,
            BudgetApproved: true);

        var riskyInput = baseInput with { SupplierRiskScore = 80m };

        var baseline = _engine.Evaluate(baseInput);
        var risky = _engine.Evaluate(riskyInput);

        risky.Score.Should().BeLessThan(baseline.Score);
        risky.RiskFlags.Should().Contain(r => r.Category == "Supplier");
    }

    [Fact]
    public void Evaluate_LateArrival_FlagsTimelineRisk()
    {
        var input = new FeasibilityInput(
            VendorEligible: true,
            VendorScorecardAverage: 70m,
            BudgetHeadroomRatio: 1.0m,
            EstimatedDutyExposureRatio: 0.12m,
            VendorLeadTimeDays: 30,
            LcFacilityAvailable: true,
            PoValueBdt: 300_000m,
            MarginPct: 0.18m,
            CostCompetitivenessIndex: 1.0m,
            SupplierRiskScore: 30m,
            NeedByDaysFromNow: 30,
            EstimatedArrivalDays: 50,
            HistoricalForecastAccuracy: 0.80m,
            PlanAligned: true,
            BudgetApproved: true);

        var result = _engine.Evaluate(input);

        result.RiskFlags.Should().Contain(r => r.Category == "Timeline");
        result.Reasons.Should().Contain(r => r.Contains("days past need-by"));
    }

    [Fact]
    public void Evaluate_CustomWeights_NormalizesCorrectly()
    {
        var input = new FeasibilityInput(
            VendorEligible: true,
            VendorScorecardAverage: 70m,
            BudgetHeadroomRatio: 1.0m,
            EstimatedDutyExposureRatio: 0.10m,
            VendorLeadTimeDays: 30,
            LcFacilityAvailable: false,
            PoValueBdt: 200_000m,
            MarginPct: 0.15m,
            SupplierRiskScore: 40m);

        var weights = new FeasibilityFactorWeights
        {
            MarginAdequacy = 50m,
            CostCompetitiveness = 10m,
            SupplierRisk = 20m,
            TimelineFit = 10m,
            HistoricalVariance = 5m,
            PlanBudgetAlignment = 5m
        };

        var result = _engine.Evaluate(input, weights);

        result.NormalizedWeights!["MarginAdequacy"].Should().BeApproximately(0.5m, 0.01m);
        result.NormalizedWeights!["CostCompetitiveness"].Should().BeApproximately(0.1m, 0.01m);
    }

    [Fact]
    public void Evaluate_NegativeMargin_FlagsRisk()
    {
        var input = new FeasibilityInput(
            VendorEligible: true,
            VendorScorecardAverage: 70m,
            BudgetHeadroomRatio: 1.0m,
            EstimatedDutyExposureRatio: 0.10m,
            VendorLeadTimeDays: 30,
            LcFacilityAvailable: true,
            PoValueBdt: 200_000m,
            MarginPct: -0.05m,
            SupplierRiskScore: 30m);

        var result = _engine.Evaluate(input);

        result.RiskFlags.Should().Contain(r => r.Category == "Financial" && r.Severity == "Critical");
        result.Reasons.Should().Contain(r => r.Contains("Low margin"));
    }

    [Fact]
    public void Evaluate_Counterfactuals_GeneratedForExpensiveLines()
    {
        var lines = new List<FeasibilityLineInput>
        {
            new(Guid.NewGuid(), "8471.30", 100m, 500m, null, null, null, 420m),
        };

        var input = new FeasibilityInput(
            VendorEligible: true,
            VendorScorecardAverage: 70m,
            BudgetHeadroomRatio: 1.0m,
            EstimatedDutyExposureRatio: 0.10m,
            VendorLeadTimeDays: 30,
            LcFacilityAvailable: true,
            PoValueBdt: 50_000m,
            MarginPct: 0.15m,
            CostCompetitivenessIndex: 1.15m,
            SupplierRiskScore: 30m,
            Lines: lines);

        var result = _engine.Evaluate(input);

        result.Counterfactuals.Should().NotBeEmpty();
        result.Counterfactuals!.First().Description.Should().Contain("alternative supplier");
    }

    [Fact]
    public void FeasibilityFactorWeights_Normalized_SumsToOne()
    {
        var weights = new FeasibilityFactorWeights
        {
            MarginAdequacy = 30m,
            CostCompetitiveness = 20m,
            SupplierRisk = 20m,
            TimelineFit = 15m,
            HistoricalVariance = 10m,
            PlanBudgetAlignment = 5m
        };

        var normalized = weights.Normalized();

        normalized.Values.Sum().Should().BeApproximately(1.0m, 0.001m);
    }
}
