namespace ProcureFlow.Modules.TradeFinance.Domain.Rules;

/// <summary>
/// LC terms to be checked against the PO/PI (BR-LC-03, BR-PO-07).
/// </summary>
public sealed record LcTermConsistencyInput(
    string LcCurrency,
    string PoCurrency,
    decimal LcAmount,
    decimal PoAmount,
    decimal TolerancePct,
    DateOnly? LcLatestShipmentDate,
    DateOnly? PoLatestShipmentDate,
    DateOnly LcExpiryDate,
    string LcPortOfLoadingName,
    string LcPortOfDischargeName,
    string PoPortOfLoadingName,
    string PoPortOfDischargeName,
    bool LcPartialShipmentAllowed,
    bool PoPartialShipmentAllowed,
    bool LcTransshipmentAllowed,
    bool PoTransshipmentAllowed,
    string LcIncoterm,
    string PoIncoterm);

/// <summary>
/// Deterministic term-consistency validator (BR-LC-03, BR-PO-07). LC terms
/// must not contradict PO terms. Returns the list of violations (empty ⇒ pass).
/// </summary>
public static class LcTermConsistencyValidator
{
    public const int DefaultExpiryBufferDays = 21;

    public static IReadOnlyList<string> Evaluate(LcTermConsistencyInput input)
    {
        var violations = new List<string>();

        if (!string.Equals(input.LcCurrency, input.PoCurrency, StringComparison.OrdinalIgnoreCase))
            violations.Add($"Currency mismatch: LC {input.LcCurrency} vs PO {input.PoCurrency} (BR-LC-03)");

        decimal lowerBound = input.PoAmount * (1m - input.TolerancePct);
        decimal upperBound = input.PoAmount * (1m + input.TolerancePct);
        if (input.LcAmount < lowerBound || input.LcAmount > upperBound)
            violations.Add($"LC amount {input.LcAmount:N2} outside PO tolerance ±{input.TolerancePct:P0} (BR-LC-03)");

        if (input.LcLatestShipmentDate.HasValue && input.PoLatestShipmentDate.HasValue &&
            input.LcLatestShipmentDate > input.PoLatestShipmentDate)
        {
            violations.Add($"Latest shipment date {input.LcLatestShipmentDate:d} later than PO {input.PoLatestShipmentDate:d} (BR-LC-03)");
        }

        DateOnly minExpiry = (input.LcLatestShipmentDate ?? input.LcExpiryDate).AddDays(DefaultExpiryBufferDays);
        if (input.LcExpiryDate < minExpiry)
            violations.Add($"Expiry {input.LcExpiryDate:d} must be ≥ latest shipment + {DefaultExpiryBufferDays} days (BR-LC-03)");

        if (!string.Equals(input.LcPortOfLoadingName, input.PoPortOfLoadingName, StringComparison.OrdinalIgnoreCase) &&
            input.PoPortOfLoadingName.Length > 0)
            violations.Add($"Port of loading mismatch: LC {input.LcPortOfLoadingName} vs PO {input.PoPortOfLoadingName} (BR-LC-03)");
        if (!string.Equals(input.LcPortOfDischargeName, input.PoPortOfDischargeName, StringComparison.OrdinalIgnoreCase) &&
            input.PoPortOfDischargeName.Length > 0)
            violations.Add($"Port of discharge mismatch: LC {input.LcPortOfDischargeName} vs PO {input.PoPortOfDischargeName} (BR-LC-03)");

        if (input.LcPartialShipmentAllowed && !input.PoPartialShipmentAllowed)
            violations.Add("LC allows partial shipment but the PO does not (BR-LC-03)");
        if (input.LcTransshipmentAllowed && !input.PoTransshipmentAllowed)
            violations.Add("LC allows transshipment but the PO does not (BR-LC-03)");

        if (!string.Equals(input.LcIncoterm, input.PoIncoterm, StringComparison.OrdinalIgnoreCase))
            violations.Add($"Incoterm mismatch: LC {input.LcIncoterm} vs PO {input.PoIncoterm} (BR-LC-03)");

        return violations;
    }
}