using ProcureFlow.Modules.OrgStructure.Application.Abstractions;
using ProcureFlow.Modules.OrgStructure.Application.Dtos;
using ProcureFlow.Modules.OrgStructure.Application.OrgNodes.Queries.GetOrgNodeById;
using ProcureFlow.Modules.OrgStructure.Domain.Entities;
using ProcureFlow.Modules.OrgStructure.Domain.Enums;
using ProcureFlow.Modules.OrgStructure.Domain.Repositories;
using ProcureFlow.Shared.Domain;
using FluentValidation;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;

namespace ProcureFlow.Modules.OrgStructure.Application.OrgNodes.Queries.GetOrgNodesByParent;

public sealed record GetOrgNodesByParentQuery(Guid? ParentId)
    : IQuery<Result<IReadOnlyList<OrgNodeResponse>>>;

internal sealed class GetOrgNodesByParentQueryValidator : AbstractValidator<GetOrgNodesByParentQuery>
{
    public GetOrgNodesByParentQueryValidator() { }
}

public sealed class GetOrgNodesByParentQueryHandler(
    IOrgNodeRepository orgNodeRepository,
    ICurrentTenant currentTenant)
    : IQueryHandler<GetOrgNodesByParentQuery, Result<IReadOnlyList<OrgNodeResponse>>>
{
    public async Task<Result<IReadOnlyList<OrgNodeResponse>>> HandleAsync(
        GetOrgNodesByParentQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<OrgNode> nodes = await orgNodeRepository.GetByParentAsync(tenantId, request.ParentId, ct);
        return Result.Success<IReadOnlyList<OrgNodeResponse>>(
            nodes.Select(GetOrgNodeByIdQueryHandler.ToResponse).ToList());
    }
}
