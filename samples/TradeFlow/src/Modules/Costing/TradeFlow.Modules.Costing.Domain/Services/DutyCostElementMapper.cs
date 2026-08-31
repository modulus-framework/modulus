using TradeFlow.Modules.Costing.Domain.Entities;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Costing.Domain.Services;

/// <summary>
/// Simple DTO for duty component data from Customs assessment.
/// Maps to <c>AssessedDutyLine</c> in the Customs module.
/// </summary>
public sealed record DutyComponentData(string Component, decimal Amount);

/// <summary>
/// Domain service that maps duty calculation results (from BoE assessment)
/// into CostElement objects with appropriate CostTreatment classification.
/// Implements the cost vs. recoverable rules from BRS §6.1:
/// - CD/RD/SD → LandedCost (always)
/// - VAT → LandedCost (default) or Recoverable (if manufacturer claiming rebate)
/// - AIT → AdvanceAsset (adjustable)
/// - AT → AdvanceAsset (adjustable)
/// </summary>
public static class DutyCostElementMapper
{
    /// <summary>
    /// Creates cost elements from assessed duty lines for a cost sheet.
    /// Each duty component becomes a cost element with driver=Direct (per line)
    /// and the appropriate treatment based on tenant policy.
    /// </summary>
    public static IReadOnlyList<CostElement> MapFromBoeAssessment(
        Guid tenantId,
        Guid fileId,
        string boeNo,
        IReadOnlyList<DutyComponentData> dutyComponents,
        decimal customsExchangeRate,
        bool isVatRecoverable = false)
    {
        if (dutyComponents == null || dutyComponents.Count == 0)
            return Array.Empty<CostElement>();

        var elements = new List<CostElement>();

        foreach (DutyComponentData dutyLine in dutyComponents)
        {
            if (dutyLine.Amount == 0m)
                continue;

            (string componentName, CostTreatment treatment) = GetComponentTreatment(
                dutyLine.Component, isVatRecoverable);

            var element = new CostElement(
                id: Guid.NewGuid(),
                name: $"Duty: {componentName} ({boeNo})",
                amountFcy: dutyLine.Amount / customsExchangeRate,
                fxRate: customsExchangeRate,
                amountBdt: dutyLine.Amount,
                driver: CostElementDriver.Direct,
                scope: CostElementScope.File,
                treatment: treatment,
                sourceDocType: "BoE",
                sourceDocNumber: boeNo,
                selectedLineIds: null);

            elements.Add(element);
        }

        return elements;
    }

    /// <summary>
    /// Determines the component display name and cost treatment based on
    /// the component type and tenant VAT policy.
    /// </summary>
    private static (string name, CostTreatment treatment) GetComponentTreatment(
        string component,
        bool isVatRecoverable)
    {
        return component.ToUpperInvariant() switch
        {
            "CD" or "CUSTOMS_DUTY" => ("Customs Duty", CostTreatment.LandedCost),
            "RD" or "REGULATORY_DUTY" => ("Regulatory Duty", CostTreatment.LandedCost),
            "SD" or "SUPPLEMENTARY_DUTY" => ("Supplementary Duty", CostTreatment.LandedCost),
            "VAT" or "VALUE_ADDED_TAX" => isVatRecoverable
                ? ("VAT (Recoverable)", CostTreatment.Recoverable)
                : ("VAT", CostTreatment.LandedCost),
            "AIT" or "ADVANCE_INCOME_TAX" => ("Advance Income Tax", CostTreatment.AdvanceAsset),
            "AT" or "ADVANCE_TAX" or "ADVANCE_VAT" => ("Advance Tax (VAT)", CostTreatment.AdvanceAsset),
            _ => (component, CostTreatment.LandedCost)
        };
    }

    /// <summary>
    /// Creates cost elements from computed duty calculation results (not yet assessed).
    /// Used for cost sheet estimates before BoE assessment is available.
    /// </summary>
    public static IReadOnlyList<CostElement> MapFromComputedDuty(
        Guid tenantId,
        Guid fileId,
        string reference,
        IReadOnlyList<DutyComponentData> components,
        decimal customsExchangeRate,
        bool isVatRecoverable = false)
    {
        if (components == null || components.Count == 0)
            return Array.Empty<CostElement>();

        var elements = new List<CostElement>();

        foreach (DutyComponentData component in components)
        {
            if (component.Amount == 0m)
                continue;

            (string componentName, CostTreatment treatment) = GetComponentTreatment(
                component.Component, isVatRecoverable);

            var element = new CostElement(
                id: Guid.NewGuid(),
                name: $"Duty (Est): {componentName} ({reference})",
                amountFcy: component.Amount / customsExchangeRate,
                fxRate: customsExchangeRate,
                amountBdt: component.Amount,
                driver: CostElementDriver.Direct,
                scope: CostElementScope.File,
                treatment: treatment,
                sourceDocType: "Estimate",
                sourceDocNumber: reference,
                selectedLineIds: null);

            elements.Add(element);
        }

        return elements;
    }
}