using ProcureFlow.Shared.Application.Abstractions.Import;

namespace ProcureFlow.Shared.Infrastructure.Import;

/// <summary>
/// Deterministic heuristic implementation of <see cref="IFeasibilityEngine"/>
/// (BRS §5.3). Scores a proposed import out of 100:
/// vendor eligibility (pass/fail gate), scorecard average (30), budget
/// headroom (30), duty exposure (25), lead time (15); LC facility acts as a
/// ±10 modifier. Verdicts: ≥70 Feasible, 40–69 Conditional (CFO override
/// required, BR-PO-02), &lt;40 NotFeasible.
/// </summary>
public sealed class HeuristicFeasibilityEngine : IFeasibilityEngine
{
    private const decimal FeasibleThreshold = 70m;
    private const decimal ConditionalThreshold = 40m;

    public FeasibilityResult Evaluate(FeasibilityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Hard gate: an ineligible vendor (blacklisted / not active) is never
        // feasible regardless of score (BR-VEN-08).
        if (!input.VendorEligible)
        {
            return new FeasibilityResult(
                FeasibilityVerdict.NotFeasible,
                0m,
                ["Vendor is not eligible (inactive or blacklisted)"]);
        }

        var reasons = new List<string>();
        decimal score = 0m;

        // Scorecard average (0–100) contributes up to 30 points.
        decimal scorecardPoints = Math.Clamp(input.VendorScorecardAverage, 0m, 100m) * 0.30m;
        score += scorecardPoints;
        if (input.VendorScorecardAverage < 50m)
        {
            reasons.Add($"Low vendor scorecard average ({input.VendorScorecardAverage:F1}/100)");
        }

        // Budget headroom ratio (available/required, 0–1+) contributes up to 30.
        decimal headroom = Math.Clamp(input.BudgetHeadroomRatio, 0m, 1m);
        score += headroom * 30m;
        if (headroom < 1m)
        {
            reasons.Add("Budget headroom is insufficient for the full PO value");
        }
        else if (headroom < 1.2m)
        {
            reasons.Add("Budget headroom is tight (<20% buffer)");
        }

        // Duty exposure ratio (duties/FOB): 0–15% is healthy (25 pts), scales down.
        decimal exposure = Math.Clamp(input.EstimatedDutyExposureRatio, 0m, 1m);
        decimal exposurePoints = exposure <= 0.15m
            ? 25m
            : Math.Max(0m, 25m - ((exposure - 0.15m) / 0.35m) * 25m);
        score += exposurePoints;
        if (exposure > 0.30m)
        {
            reasons.Add($"High duty exposure ({exposure * 100m:F1}% of FOB)");
        }

        // Lead time: ≤30 days earns full 15, −1 point/day beyond, floor 0.
        decimal leadTimePoints = input.VendorLeadTimeDays <= 30
            ? 15m
            : Math.Max(0m, 15m - (input.VendorLeadTimeDays - 30));
        score += leadTimePoints;
        if (input.VendorLeadTimeDays > 60)
        {
            reasons.Add($"Long lead time ({input.VendorLeadTimeDays} days)");
        }

        // LC facility availability modifier.
        if (input.LcFacilityAvailable)
        {
            score += 10m;
        }
        else
        {
            score = Math.Max(0m, score - 10m);
            reasons.Add("No LC facility headroom available");
        }

        score = Math.Round(Math.Clamp(score, 0m, 100m), 2);

        var verdict = score >= FeasibleThreshold
            ? FeasibilityVerdict.Feasible
            : score >= ConditionalThreshold
                ? FeasibilityVerdict.Conditional
                : FeasibilityVerdict.NotFeasible;

        if (reasons.Count == 0)
        {
            reasons.Add("All signals within normal ranges");
        }

        return new FeasibilityResult(verdict, score, reasons);
    }
}
