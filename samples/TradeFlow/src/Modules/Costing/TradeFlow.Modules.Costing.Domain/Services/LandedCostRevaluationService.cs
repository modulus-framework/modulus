using TradeFlow.Modules.Costing.Domain.Entities;
using TradeFlow.Modules.Costing.Domain.Repositories;

namespace TradeFlow.Modules.Costing.Domain.Services;

/// <summary>
/// Domain service that performs the periodic landed-cost FX revaluation
/// (period close): revalues FX-denominated cost elements on finalized sheets
/// against current rates and records the variances on a <see cref="RevaluationRun"/>.
/// </summary>
public interface ILandedCostRevaluationService
{
    /// <summary>Builds (but does not persist) a completed revaluation run for the period.</summary>
    Task<RevaluationRun> RevaluatePeriodAsync(
        Guid tenantId,
        DateOnly periodEnd,
        IReadOnlyDictionary<string, decimal> currentFxRates,
        CancellationToken ct = default);
}

/// <summary>
/// Default implementation: scans finalized/adjusted cost sheets, skips
/// BDT-denominated elements (duty assessed in BDT does not float), converts
/// each FX element at the new rate and records material variances.
/// </summary>
public sealed class LandedCostRevaluationService(ILandedCostSheetRepository sheetRepository)
    : ILandedCostRevaluationService
{
    private const decimal MaterialityThreshold = 0.01m;

    public async Task<RevaluationRun> RevaluatePeriodAsync(
        Guid tenantId,
        DateOnly periodEnd,
        IReadOnlyDictionary<string, decimal> currentFxRates,
        CancellationToken ct = default)
    {
        RevaluationRun run = RevaluationRun.Start(tenantId, periodEnd);

        IReadOnlyList<LandedCostSheet> sheets = await sheetRepository.GetFinalizedByTenantAsync(tenantId, ct);

        foreach (LandedCostSheet sheet in sheets)
        {
            foreach (CostElement element in sheet.Elements)
            {
                if (element.AmountFcy == 0m)
                    continue;

                if (string.IsNullOrWhiteSpace(element.Currency) ||
                    element.Currency.Equals("BDT", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!currentFxRates.TryGetValue(element.Currency.ToUpperInvariant(), out decimal newFxRate))
                    continue;

                decimal newAmountBdt = decimal.Round(element.AmountFcy * newFxRate, 4, MidpointRounding.ToEven);
                decimal gainLoss = newAmountBdt - element.AmountBdt;
                if (Math.Abs(gainLoss) <= MaterialityThreshold)
                    continue;

                run.AddVariance(sheet.Id, sheet.SheetNumber, element.Id, element.Name, element.Currency,
                    element.AmountFcy, element.FxRate, element.AmountBdt, newFxRate, newAmountBdt);
            }
        }

        run.Complete(sheets.Count);
        return run;
    }
}