using Modulus.Events.Abstractions;
using TradeFlow.Modules.Customs.Domain.Entities;

namespace TradeFlow.Modules.Customs.Application.IntegrationEvents;

public sealed record BoeAssessedIntegrationEvent(
    Guid BoeId,
    Guid TenantId,
    Guid? FileId,
    string BoeNo,
    decimal AssessedTti,
    IReadOnlyList<AssessedDutyLine> AssessedDutyLines,
    decimal CustomsExchangeRate,
    DateTime OccurredAtUtc) : IntegrationEventBase("Customs.BoeAssessed.v1")
{
    public Guid BoeId { get; } = BoeId;
    public Guid TenantId { get; } = TenantId;
    public Guid? FileId { get; } = FileId;
    public string BoeNo { get; } = BoeNo;
    public decimal AssessedTti { get; } = AssessedTti;
    public IReadOnlyList<AssessedDutyLine> AssessedDutyLines { get; } = AssessedDutyLines;
    public decimal CustomsExchangeRate { get; } = CustomsExchangeRate;
    public DateTime OccurredAtUtc { get; } = OccurredAtUtc;
}

public sealed record BoeReleasedIntegrationEvent(
    Guid BoeId,
    Guid TenantId,
    string BoeNo,
    DateTime OccurredAtUtc) : IntegrationEventBase("Customs.BoeReleased.v1")
{
    public Guid BoeId { get; } = BoeId;
    public Guid TenantId { get; } = TenantId;
    public string BoeNo { get; } = BoeNo;
    public DateTime OccurredAtUtc { get; } = OccurredAtUtc;
}

public sealed record DutyVarianceOpenedIntegrationEvent(
    Guid BoeId,
    Guid BoeLineId,
    decimal VarianceAmount,
    DateTime OccurredAtUtc) : IntegrationEventBase("Customs.DutyVarianceOpened.v1")
{
    public Guid BoeId { get; } = BoeId;
    public Guid BoeLineId { get; } = BoeLineId;
    public decimal VarianceAmount { get; } = VarianceAmount;
    public DateTime OccurredAtUtc { get; } = OccurredAtUtc;
}

/// <summary>
/// Published when the Tax Officer counterposts an AIT/AT adjustment per return
/// period. Finance subscribes to post Dr Income Tax Expense / Cr Advance Tax Asset.
/// </summary>
public sealed record AitAtAdjustmentRecordedIntegrationEvent(
    Guid EntryId,
    Guid TenantId,
    Guid CompanyId,
    int FiscalYear,
    string Component,
    decimal Amount,
    string ReturnPeriod,
    DateOnly BookedOn,
    DateTime OccurredAtUtc) : IntegrationEventBase("Customs.AitAtAdjustmentRecorded.v1")
{
    public Guid EntryId { get; } = EntryId;
    public Guid TenantId { get; } = TenantId;
    public Guid CompanyId { get; } = CompanyId;
    public int FiscalYear { get; } = FiscalYear;
    public string Component { get; } = Component;
    public decimal Amount { get; } = Amount;
    public string ReturnPeriod { get; } = ReturnPeriod;
    public DateOnly BookedOn { get; } = BookedOn;
    public DateTime OccurredAtUtc { get; } = OccurredAtUtc;
}