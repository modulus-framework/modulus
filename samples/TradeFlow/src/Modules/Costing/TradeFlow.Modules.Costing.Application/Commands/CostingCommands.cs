using TradeFlow.Modules.Costing.Application.Dtos;
using TradeFlow.Modules.Costing.Domain.Entities;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Costing.Application.Commands;

public sealed record CreateLandedCostSheetCommand(
    Guid FileId,
    string SheetNumber,
    string Currency,
    IReadOnlyList<CostSheetLineInput> Lines) : Modulus.Mediator.Abstractions.ICommand<Result<LandedCostSheetResponse>>;

public sealed record CostSheetLineInput(
    Guid SourceLineId,
    decimal GoodsValueFcy,
    decimal GoodsValueBdt,
    decimal ReceivedQty,
    decimal NetWeightKg,
    decimal GrossWeightKg,
    decimal VolumeCbm,
    decimal ContainerShare);

public sealed record AddCostElementCommand(
    Guid SheetId,
    string Name,
    decimal AmountFcy,
    decimal FxRate,
    decimal AmountBdt,
    CostElementDriver Driver,
    CostElementScope Scope,
    CostTreatment Treatment,
    string SourceDocType,
    string SourceDocNumber,
    IReadOnlyList<Guid>? SelectedLineIds,
    string? Currency = null) : Modulus.Mediator.Abstractions.ICommand<Result<LandedCostSheetResponse>>;

public sealed record AllocateCostsCommand(
    Guid SheetId) : Modulus.Mediator.Abstractions.ICommand<Result<LandedCostSheetResponse>>;

public sealed record FinalizeCostSheetCommand(
    Guid SheetId) : Modulus.Mediator.Abstractions.ICommand<Result<LandedCostSheetResponse>>;

public sealed record OpenAdjustmentCommand(
    Guid SheetId) : Modulus.Mediator.Abstractions.ICommand<Result<LandedCostSheetResponse>>;