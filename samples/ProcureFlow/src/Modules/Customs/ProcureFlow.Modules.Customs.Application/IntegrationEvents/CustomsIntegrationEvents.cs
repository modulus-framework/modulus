using Modulus.Events.Abstractions;

namespace ProcureFlow.Modules.Customs.Application.IntegrationEvents;

public sealed record BoeAssessedIntegrationEvent(
    Guid BoeId,
    Guid TenantId,
    Guid? FileId,
    decimal AssessedTti,
    DateTime OccurredAtUtc) : IntegrationEventBase("Customs.BoeAssessed.v1")
{
    public Guid BoeId { get; } = BoeId;
    public Guid TenantId { get; } = TenantId;
    public Guid? FileId { get; } = FileId;
    public decimal AssessedTti { get; } = AssessedTti;
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