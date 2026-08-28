using Modulus.Events.Abstractions;

namespace ProcureFlow.Modules.Costing.Application.IntegrationEvents;

public sealed record CostSheetFinalizedIntegrationEvent(
    Guid SheetId,
    Guid TenantId,
    Guid FileId,
    string SheetNumber,
    int SheetVersion,
    DateTime OccurredAtUtc) : IntegrationEventBase("Costing.CostSheetFinalized.v1")
{
    public Guid SheetId { get; } = SheetId;
    public Guid TenantId { get; } = TenantId;
    public Guid FileId { get; } = FileId;
    public string SheetNumber { get; } = SheetNumber;
    public int SheetVersion { get; } = SheetVersion;
    public DateTime OccurredAtUtc { get; } = OccurredAtUtc;
}

public sealed record CostSheetAdjustedIntegrationEvent(
    Guid SheetId,
    Guid TenantId,
    Guid FileId,
    string SheetNumber,
    int SheetVersion,
    DateTime OccurredAtUtc) : IntegrationEventBase("Costing.CostSheetAdjusted.v1")
{
    public Guid SheetId { get; } = SheetId;
    public Guid TenantId { get; } = TenantId;
    public Guid FileId { get; } = FileId;
    public string SheetNumber { get; } = SheetNumber;
    public int SheetVersion { get; } = SheetVersion;
    public DateTime OccurredAtUtc { get; } = OccurredAtUtc;
}