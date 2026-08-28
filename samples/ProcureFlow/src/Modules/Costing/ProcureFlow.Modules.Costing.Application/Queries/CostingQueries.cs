using ProcureFlow.Modules.Costing.Application.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Costing.Application.Queries;

public sealed record GetLandedCostSheetQuery(Guid SheetId) : Modulus.Mediator.Abstractions.IQuery<Result<LandedCostSheetResponse>>;

public sealed record GetLandedCostSheetByFileQuery(Guid FileId) : Modulus.Mediator.Abstractions.IQuery<Result<LandedCostSheetResponse>>;

public sealed record ListLandedCostSheetsQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<LandedCostSheetResponse>>>;