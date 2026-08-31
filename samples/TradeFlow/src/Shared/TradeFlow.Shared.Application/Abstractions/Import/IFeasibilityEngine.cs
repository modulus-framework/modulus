namespace TradeFlow.Shared.Application.Abstractions.Import;

/// <summary>
/// BRS §5.3 feasibility seam. Procurement gates import purchase orders on
/// this engine (BR-PO-02): it scores a proposed import against vendor,
/// budget, duty-exposure and facility signals and returns a verdict plus an
/// immutable snapshot stored on the PO. The P1 implementation is the
/// deterministic heuristic in Shared.Infrastructure; the Intelligence module
/// replaces it in a later phase without touching Procurement.
/// </summary>
public interface IFeasibilityEngine
{
    FeasibilityResult Evaluate(FeasibilityInput input);
    FeasibilityResult Evaluate(FeasibilityInput input, FeasibilityFactorWeights weights);
}

public sealed record FeasibilityInput(
    bool VendorEligible,
    decimal VendorScorecardAverage,
    decimal BudgetHeadroomRatio,
    decimal EstimatedDutyExposureRatio,
    int VendorLeadTimeDays,
    bool LcFacilityAvailable,
    decimal PoValueBdt,
    // ── Enhanced fields (doc 07 §7.3) ──
    decimal? MarginPct = null,
    decimal? CostCompetitivenessIndex = null,
    decimal? SupplierRiskScore = null,
    int? NeedByDaysFromNow = null,
    int? EstimatedArrivalDays = null,
    decimal? HistoricalForecastAccuracy = null,
    bool? PlanAligned = null,
    bool? BudgetApproved = null,
    IReadOnlyList<FeasibilityLineInput>? Lines = null);

/// <summary>Per-line input for detailed feasibility scoring.</summary>
public sealed record FeasibilityLineInput(
    Guid LineId,
    string HsCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal? ForecastLandedUnitCost,
    decimal? StandardPrice,
    decimal? LastImportLandedCost,
    decimal? BestAlternativeLandedCost);

/// <summary>
/// Tenant-tunable factor weights (doc 07 §7.3). Defaults sum to 100.
/// Weights are normalized at evaluation time so any positive values work.
/// </summary>
public sealed record FeasibilityFactorWeights
{
    public decimal MarginAdequacy { get; init; } = 30m;
    public decimal CostCompetitiveness { get; init; } = 20m;
    public decimal SupplierRisk { get; init; } = 20m;
    public decimal TimelineFit { get; init; } = 15m;
    public decimal HistoricalVariance { get; init; } = 10m;
    public decimal PlanBudgetAlignment { get; init; } = 5m;

    /// <summary>Returns normalized weights (each factor's proportion of the total).</summary>
    public IReadOnlyDictionary<string, decimal> Normalized()
    {
        decimal total = MarginAdequacy + CostCompetitiveness + SupplierRisk
                      + TimelineFit + HistoricalVariance + PlanBudgetAlignment;
        if (total <= 0m) total = 1m;
        return new Dictionary<string, decimal>
        {
            ["MarginAdequacy"] = MarginAdequacy / total,
            ["CostCompetitiveness"] = CostCompetitiveness / total,
            ["SupplierRisk"] = SupplierRisk / total,
            ["TimelineFit"] = TimelineFit / total,
            ["HistoricalVariance"] = HistoricalVariance / total,
            ["PlanBudgetAlignment"] = PlanBudgetAlignment / total,
        };
    }
}

/// <summary>Verdict thresholds: score ≥ 70 Feasible, 40–69 Conditional (needs CFO
/// override to proceed), &lt; 40 NotFeasible.</summary>
public enum FeasibilityVerdict
{
    Feasible = 1,
    Conditional = 2,
    NotFeasible = 3,
}

/// <summary>Individual factor scoring detail for audit lineage.</summary>
public sealed record FeasibilityFactor(
    string Name,
    decimal RawValue,
    decimal NormalizedScore,
    decimal WeightedContribution,
    string Description);

/// <summary>Risk flag surfaced in the feasibility snapshot.</summary>
public sealed record FeasibilityRiskFlag(
    string Category,
    string Message,
    string Severity);

/// <summary>Counterfactual hint: what-if an alternative were chosen.</summary>
public sealed record FeasibilityCounterfactual(
    string Description,
    decimal EstimatedScoreDelta,
    decimal? EstimatedCostDelta);

public sealed record FeasibilityResult(
    FeasibilityVerdict Verdict,
    decimal Score,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<FeasibilityFactor>? Factors = null,
    IReadOnlyList<FeasibilityRiskFlag>? RiskFlags = null,
    IReadOnlyList<FeasibilityCounterfactual>? Counterfactuals = null,
    IReadOnlyDictionary<string, decimal>? NormalizedWeights = null)
{
    public bool RequiresCfoOverride => Verdict == FeasibilityVerdict.Conditional || Verdict == FeasibilityVerdict.NotFeasible;
}
