using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Costing.Application.Dtos;
using TradeFlow.Modules.Costing.Application.Queries;
using TradeFlow.Modules.Costing.Domain.Entities;
using TradeFlow.Modules.Costing.Domain.Repositories;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Costing.Application.Queries;

public sealed class GetLandedCostSheetHandler(ILandedCostSheetRepository repository) : IQueryHandler<GetLandedCostSheetQuery, Result<LandedCostSheetResponse>>
{
    public async Task<Result<LandedCostSheetResponse>> HandleAsync(GetLandedCostSheetQuery query, CancellationToken ct)
    {
        LandedCostSheet? sheet = await repository.GetByIdAsync(query.SheetId, ct);
        return sheet is null
            ? Result.Failure<LandedCostSheetResponse>(Error.NotFound("Lcs.NotFound", "Cost sheet not found"))
            : Result.Success(CostingResponseFactory.ToSheetResponse(sheet));
    }
}

public sealed class GetLandedCostSheetByFileHandler(
    ILandedCostSheetRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetLandedCostSheetByFileQuery, Result<LandedCostSheetResponse>>
{
    public async Task<Result<LandedCostSheetResponse>> HandleAsync(GetLandedCostSheetByFileQuery query, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        LandedCostSheet? sheet = await repository.GetByFileAsync(tenantId, query.FileId, ct);
        return sheet is null
            ? Result.Failure<LandedCostSheetResponse>(Error.NotFound("Lcs.NotFound", "No cost sheet for file"))
            : Result.Success(CostingResponseFactory.ToSheetResponse(sheet));
    }
}

public sealed class ListLandedCostSheetsHandler(
    ILandedCostSheetRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<ListLandedCostSheetsQuery, Result<IReadOnlyList<LandedCostSheetResponse>>>
{
    public async Task<Result<IReadOnlyList<LandedCostSheetResponse>>> HandleAsync(ListLandedCostSheetsQuery query, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<LandedCostSheet> sheets = await repository.GetByTenantAsync(tenantId, ct);
        return Result.Success<IReadOnlyList<LandedCostSheetResponse>>(sheets.Select(CostingResponseFactory.ToSheetResponse).ToArray());
    }
}