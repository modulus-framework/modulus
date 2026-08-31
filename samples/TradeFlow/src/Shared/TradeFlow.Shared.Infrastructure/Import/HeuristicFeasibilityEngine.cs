using TradeFlow.Shared.Application.Abstractions.Import;

namespace TradeFlow.Shared.Infrastructure.Import;

/// <summary>
/// Deterministic heuristic implementation of <see cref="IFeasibilityEngine"/>
/// (doc 07 §7.3). Scores a proposed import 0–100 using tenant-tunable weights.
/// Default weights: Margin 30, CostCompetitiveness 20, SupplierRisk 20,
/// Timeline 15, HistoricalVariance 10, PlanBudget 5.
/// Verdicts: ≥70 Feasible, 40–69 Conditional (CFO override required, BR-PO-02), &lt;40 NotFeasible.
/// </summary>
public sealed class HeuristicFeasibilityEngine : IFeasibilityEngine
{
    private const decimal FeasibleThreshold = 70m;
    private const decimal ConditionalThreshold = 40m;
    private static readonly FeasibilityFactorWeights DefaultWeights = new();

    public FeasibilityResult Evaluate(FeasibilityInput input)
        => Evaluate(input, DefaultWeights);

    public FeasibilityResult Evaluate(FeasibilityInput input, FeasibilityFactorWeights weights)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(weights);

        // Hard gate: an ineligible vendor (blacklisted / not active) is never
        // feasible regardless of score (BR-VEN-08).
        if (!input.VendorEligible)
        {
            return new FeasibilityResult(
                FeasibilityVerdict.NotFeasible,
                0m,
                ["Vendor is not eligible (inactive or blacklisted)"],
                NormalizedWeights: weights.Normalized());
        }

        var reasons = new List<string>();
        var factors = new List<FeasibilityFactor>();
        var riskFlags = new List<FeasibilityRiskFlag>();
        var counterfactuals = new List<FeasibilityCounterfactual>();
        decimal score = 0m;
        var normalized = weights.Normalized();

        // ── Factor 1: Margin Adequacy (default weight 30) ──
        decimal marginScore = ComputeMarginScore(input, reasons, riskFlags);
        decimal marginContrib = marginScore * normalized["MarginAdequacy"];
        score += marginContrib;
        factors.Add(new FeasibilityFactor(
            "MarginAdequacy", input.MarginPct ?? 0m, marginScore, marginContrib,
            input.MarginPct.HasValue
                ? $"Margin {input.MarginPct.Value * 100m:F1}% vs category target"
                : "Margin data unavailable — using neutral score"));

        // ── Factor 2: Cost Competitiveness (default weight 20) ──
        decimal costScore = ComputeCostCompetitivenessScore(input, reasons, counterfactuals);
        decimal costContrib = costScore * normalized["CostCompetitiveness"];
        score += costContrib;
        factors.Add(new FeasibilityFactor(
            "CostCompetitiveness", input.CostCompetitivenessIndex ?? 0m, costScore, costContrib,
            "Price vs last-3 imports & best alternative supplier landed cost"));

        // ── Factor 3: Supplier Risk (default weight 20) ──
        decimal riskScore = ComputeSupplierRiskScore(input, reasons, riskFlags);
        decimal riskContrib = riskScore * normalized["SupplierRisk"];
        score += riskContrib;
        factors.Add(new FeasibilityFactor(
            "SupplierRisk", input.SupplierRiskScore ?? 50m, riskScore, riskContrib,
            "Inverse of supplier risk score (higher = less risky = better)"));

        // ── Factor 4: Timeline Fit (default weight 15) ──
        decimal timelineScore = ComputeTimelineScore(input, reasons, riskFlags);
        decimal timelineContrib = timelineScore * normalized["TimelineFit"];
        score += timelineContrib;
        factors.Add(new FeasibilityFactor(
            "TimelineFit", input.EstimatedArrivalDays ?? 0m, timelineScore, timelineContrib,
            "Need-by date vs estimated arrival timeline"));

        // ── Factor 5: Historical Variance (default weight 10) ──
        decimal varianceScore = ComputeVarianceScore(input, reasons);
        decimal varianceContrib = varianceScore * normalized["HistoricalVariance"];
        score += varianceContrib;
        factors.Add(new FeasibilityFactor(
            "HistoricalVariance", input.HistoricalForecastAccuracy ?? 0m, varianceScore, varianceContrib,
            "Forecast accuracy based on item/lane historical data"));

        // ── Factor 6: Plan & Budget Alignment (default weight 5) ──
        decimal alignmentScore = ComputeAlignmentScore(input, reasons);
        decimal alignmentContrib = alignmentScore * normalized["PlanBudgetAlignment"];
        score += alignmentContrib;
        factors.Add(new FeasibilityFactor(
            "PlanBudgetAlignment", (input.PlanAligned == true && input.BudgetApproved == true) ? 100m : 0m,
            alignmentContrib / (normalized["PlanBudgetAlignment"] > 0 ? normalized["PlanBudgetAlignment"] : 1m),
            alignmentContrib,
            "Import plan and budget approval alignment"));

        // ── Legacy modifiers (LC facility, budget headroom, duty exposure) ──
        // These complement the new factor model for backward compatibility.
        if (input.LcFacilityAvailable)
        {
            score += 5m;
        }
        else
        {
            score = Math.Max(0m, score - 5m);
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

        return new FeasibilityResult(
            verdict, score, reasons,
            Factors: factors,
            RiskFlags: riskFlags,
            Counterfactuals: counterfactuals,
            NormalizedWeights: normalized);
    }

    private static decimal ComputeMarginScore(
        FeasibilityInput input, List<string> reasons, List<FeasibilityRiskFlag> riskFlags)
    {
        if (!input.MarginPct.HasValue)
            return 50m; // neutral when data unavailable

        decimal margin = input.MarginPct.Value;
        // Margin ≥ 25% → 100, 15–25% → linear 60–100, 5–15% → linear 20–60, <5% → 0–20
        decimal score = margin switch
        {
            >= 0.25m => 100m,
            >= 0.15m => 60m + (margin - 0.15m) / 0.10m * 40m,
            >= 0.05m => 20m + (margin - 0.05m) / 0.10m * 40m,
            >= 0m    => margin / 0.05m * 20m,
            _        => 0m // negative margin = loss
        };

        if (margin < 0.10m)
            reasons.Add($"Low margin ({margin * 100m:F1}% — below 10% target)");
        if (margin < 0m)
            riskFlags.Add(new FeasibilityRiskFlag("Financial", $"Negative margin ({margin * 100m:F1}%)", "Critical"));

        return Math.Clamp(score, 0m, 100m);
    }

    private static decimal ComputeCostCompetitivenessScore(
        FeasibilityInput input, List<string> reasons, List<FeasibilityCounterfactual> counterfactuals)
    {
        if (!input.CostCompetitivenessIndex.HasValue)
            return 50m;

        // Index: 1.0 = on par with alternatives, <1.0 = cheaper (better), >1.0 = more expensive
        decimal index = input.CostCompetitivenessIndex.Value;
        decimal score = index switch
        {
            <= 0.80m => 100m,  // ≥20% cheaper
            <= 0.95m => 70m + (0.95m - index) / 0.15m * 30m,
            <= 1.00m => 50m + (1.00m - index) / 0.05m * 20m,
            <= 1.10m => 30m + (1.10m - index) / 0.10m * 20m,
            <= 1.25m => 10m + (1.25m - index) / 0.15m * 20m,
            _        => 0m
        };

        if (index > 1.10m)
            reasons.Add($"Price is {(index - 1m) * 100m:F1}% above comparable alternatives");

        if (input.Lines is { Count: > 0 })
        {
            foreach (var line in input.Lines.Where(l => l.BestAlternativeLandedCost.HasValue && l.UnitPrice > 0))
            {
                decimal delta = (line.UnitPrice - line.BestAlternativeLandedCost!.Value) / line.BestAlternativeLandedCost!.Value;
                if (delta > 0.02m)
                {
                    counterfactuals.Add(new FeasibilityCounterfactual(
                        $"Line {line.LineId}: alternative supplier landed cost est. {delta * 100m:F1}% lower",
                        delta * 50m, // rough score impact
                        line.BestAlternativeLandedCost!.Value * line.Quantity - line.UnitPrice * line.Quantity));
                }
            }
        }

        return Math.Clamp(score, 0m, 100m);
    }

    private static decimal ComputeSupplierRiskScore(
        FeasibilityInput input, List<string> reasons, List<FeasibilityRiskFlag> riskFlags)
    {
        if (!input.SupplierRiskScore.HasValue)
            return 50m;

        // Supplier risk score: 0 = no risk, 100 = extreme risk. We invert for feasibility.
        decimal risk = input.SupplierRiskScore.Value;
        decimal score = 100m - risk;

        if (risk > 70m)
        {
            reasons.Add($"High supplier risk ({risk:F0}/100)");
            riskFlags.Add(new FeasibilityRiskFlag("Supplier", $"Risk score {risk:F0}/100", "High"));
        }
        else if (risk > 50m)
        {
            reasons.Add($"Moderate supplier risk ({risk:F0}/100)");
        }

        return Math.Clamp(score, 0m, 100m);
    }

    private static decimal ComputeTimelineScore(
        FeasibilityInput input, List<string> reasons, List<FeasibilityRiskFlag> riskFlags)
    {
        if (!input.NeedByDaysFromNow.HasValue || !input.EstimatedArrivalDays.HasValue)
            return 50m;

        int needBy = input.NeedByDaysFromNow.Value;
        int arrival = input.EstimatedArrivalDays.Value;
        int delta = arrival - needBy;

        // Arrives early or on time → full score; late → penalized
        decimal score = delta switch
        {
            <= -14 => 100m,  // 2+ weeks early
            <= 0   => 70m + (14m + delta) / 14m * 30m,  // on time to 2 weeks early
            <= 7   => 50m - delta * 5m,  // 1-7 days late
            <= 21  => 15m - (delta - 7) * 1.5m,  // 1-3 weeks late
            _      => 0m   // >3 weeks late
        };

        if (delta > 7)
        {
            reasons.Add($"Estimated arrival is {delta} days past need-by date");
            riskFlags.Add(new FeasibilityRiskFlag("Timeline", $"Arrival {delta} days late", "High"));
        }
        else if (delta > 0)
        {
            reasons.Add($"Estimated arrival is {delta} days past need-by date (tight)");
        }

        return Math.Clamp(score, 0m, 100m);
    }

    private static decimal ComputeVarianceScore(
        FeasibilityInput input, List<string> reasons)
    {
        if (!input.HistoricalForecastAccuracy.HasValue)
            return 50m;

        // Accuracy: 1.0 = perfect forecast, 0.0 = 100% variance
        decimal accuracy = Math.Clamp(input.HistoricalForecastAccuracy.Value, 0m, 1m);
        decimal score = accuracy * 100m;

        if (accuracy < 0.70m)
            reasons.Add($"Low forecast accuracy ({accuracy * 100m:F0}% — high historical variance)");

        return score;
    }

    private static decimal ComputeAlignmentScore(
        FeasibilityInput input, List<string> reasons)
    {
        bool planOk = input.PlanAligned ?? true;
        bool budgetOk = input.BudgetApproved ?? true;

        if (planOk && budgetOk) return 100m;
        if (!planOk && !budgetOk)
        {
            reasons.Add("PO not aligned with import plan or budget approval");
            return 0m;
        }
        if (!planOk)
        {
            reasons.Add("PO not aligned with import plan");
            return 40m;
        }
        // !budgetOk
        reasons.Add("Budget approval pending");
        return 50m;
    }
}
