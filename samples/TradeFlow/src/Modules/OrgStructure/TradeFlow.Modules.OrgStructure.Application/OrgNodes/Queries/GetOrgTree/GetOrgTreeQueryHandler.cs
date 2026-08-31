using TradeFlow.Modules.OrgStructure.Application.Abstractions;
using TradeFlow.Modules.OrgStructure.Application.Dtos;
using TradeFlow.Modules.OrgStructure.Application.OrgNodes.Queries.GetOrgNodeById;
using TradeFlow.Modules.OrgStructure.Domain.Entities;
using TradeFlow.Modules.OrgStructure.Domain.Repositories;
using TradeFlow.Shared.Domain;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;

namespace TradeFlow.Modules.OrgStructure.Application.OrgNodes.Queries.GetOrgTree;

public sealed record GetOrgTreeQuery
    : IQuery<Result<IReadOnlyList<OrgNodeResponse>>>;

public sealed class GetOrgTreeQueryHandler(
    IOrgNodeRepository orgNodeRepository,
    ICurrentTenant currentTenant)
    : IQueryHandler<GetOrgTreeQuery, Result<IReadOnlyList<OrgNodeResponse>>>
{
    public async Task<Result<IReadOnlyList<OrgNodeResponse>>> HandleAsync(
        GetOrgTreeQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<OrgNode> nodes = await orgNodeRepository.GetAllAsync(tenantId, ct);
        return Result.Success<IReadOnlyList<OrgNodeResponse>>(
            nodes.OrderBy(n => n.Depth).ThenBy(n => n.Code)
                .Select(GetOrgNodeByIdQueryHandler.ToResponse).ToList());
    }
}
