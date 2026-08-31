using TradeFlow.Modules.Costing.Application.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Costing.Application.Queries;

public sealed record GetLandedCostSheetQuery(Guid SheetId) : Modulus.Mediator.Abstractions.IQuery<Result<LandedCostSheetResponse>>;

public sealed record GetLandedCostSheetByFileQuery(Guid FileId) : Modulus.Mediator.Abstractions.IQuery<Result<LandedCostSheetResponse>>;

public sealed record ListLandedCostSheetsQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<LandedCostSheetResponse>>>;