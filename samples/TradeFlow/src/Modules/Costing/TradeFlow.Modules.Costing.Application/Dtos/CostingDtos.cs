using TradeFlow.Modules.Costing.Domain.Entities;

namespace TradeFlow.Modules.Costing.Application.Dtos;

public sealed record CostSheetLineResponse(
    Guid Id,
    Guid SourceLineId,
    decimal GoodsValueFcy,
    decimal GoodsValueBdt,
    decimal ReceivedQty,
    decimal TotalLandedCostBdt,
    decimal UnitLandedCost,
    IReadOnlyList<LineAllocationResponse> Allocations);

public sealed record LineAllocationResponse(
    Guid ElementId,
    string ElementName,
    decimal AmountBdt,
    CostTreatment Treatment,
    bool IsResidual);

public sealed record CostElementResponse(
    Guid Id,
    string Name,
    decimal AmountFcy,
    decimal FxRate,
    decimal AmountBdt,
    CostElementDriver Driver,
    CostElementScope Scope,
    CostTreatment Treatment,
    string SourceDocType,
    string SourceDocNumber);

public sealed record LandedCostSheetResponse(
    Guid Id,
    Guid TenantId,
    Guid FileId,
    string SheetNumber,
    string Currency,
    CostSheetStatus Status,
    int SheetVersion,
    DateTime? FinalizedAtUtc,
    IReadOnlyList<CostSheetLineResponse> Lines,
    IReadOnlyList<CostElementResponse> Elements);