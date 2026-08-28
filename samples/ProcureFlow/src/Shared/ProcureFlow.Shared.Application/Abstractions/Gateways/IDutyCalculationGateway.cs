namespace ProcureFlow.Shared.Application.Abstractions.Gateways;

/// <summary>
/// Read-side gateway into the Customs module (BRS §5.3). Exposes duty
/// estimation for feasibility checks (Procurement) and landed-cost
/// computation (Costing). Implemented by Customs.Infrastructure.
/// </summary>
public interface IDutyCalculationGateway
{
    /// <summary>
    /// §23.1: estimates the total duty+tax burden for one HS-code line at a
    /// given assessment date, using effective-dated rates (BR-DS-01).
    /// </summary>
    Task<DutyEstimateResult> EstimateAsync(DutyEstimateRequest request, CancellationToken ct = default);
}

public sealed record DutyEstimateRequest(
    Guid TenantId,
    string HsCode,
    decimal Quantity,
    decimal UnitPrice,
    string Currency,
    decimal ExchangeRateToBdt,
    decimal FreightShare,
    decimal InsuranceShare,
    DateOnly AssessmentDate);

public sealed record DutyEstimateResult(
    decimal AssessableValueBdt,
    decimal TotalDutyBdt,
    IReadOnlyList<DutyComponentEstimate> Components);

public sealed record DutyComponentEstimate(string Component, string RateDescription, decimal AmountBdt);
