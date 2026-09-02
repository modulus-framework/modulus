using Modulus.Core.Abstractions.Domain;

namespace TradeFlow.Modules.Costing.Domain.Events;

/// <summary>
/// Raised when a periodic landed cost revaluation run completes.
/// Carries tenant-wide FX gain/loss totals for P&L variance reporting.
/// </summary>
public sealed record LandedCostRevaluedDomainEvent(
    Guid RunId,
    Guid TenantId,
    DateOnly PeriodEnd,
    int SheetsScanned,
    int VarianceCount,
    decimal TotalOriginalValueBdt,
    decimal TotalRevaluedValueBdt,
    decimal TotalFxGainLossBdt) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}