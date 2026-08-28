namespace ProcureFlow.Shared.Application.Abstractions.Import;

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
}

public sealed record FeasibilityInput(
    bool VendorEligible,
    decimal VendorScorecardAverage,
    decimal BudgetHeadroomRatio,
    decimal EstimatedDutyExposureRatio,
    int VendorLeadTimeDays,
    bool LcFacilityAvailable,
    decimal PoValueBdt);

/// <summary>
/// Verdict thresholds: score ≥ 70 Feasible, 40–69 Conditional (needs CFO
/// override to proceed), &lt; 40 NotFeasible.
/// </summary>
public enum FeasibilityVerdict
{
    Feasible = 1,
    Conditional = 2,
    NotFeasible = 3,
}

public sealed record FeasibilityResult(
    FeasibilityVerdict Verdict,
    decimal Score,
    IReadOnlyList<string> Reasons)
{
    public bool RequiresCfoOverride => Verdict == FeasibilityVerdict.Conditional || Verdict == FeasibilityVerdict.NotFeasible;
}
