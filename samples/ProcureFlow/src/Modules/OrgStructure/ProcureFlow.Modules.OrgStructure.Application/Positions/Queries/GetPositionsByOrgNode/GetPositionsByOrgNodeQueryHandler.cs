using ProcureFlow.Modules.OrgStructure.Application.Abstractions;
using ProcureFlow.Modules.OrgStructure.Application.Dtos;
using ProcureFlow.Modules.OrgStructure.Domain.Entities;
using ProcureFlow.Modules.OrgStructure.Domain.Repositories;
using ProcureFlow.Shared.Domain;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;

namespace ProcureFlow.Modules.OrgStructure.Application.Positions.Queries.GetPositionsByOrgNode;

public sealed record GetPositionsByOrgNodeQuery(Guid OrgNodeId)
    : IQuery<Result<IReadOnlyList<PositionResponse>>>;

public sealed class GetPositionsByOrgNodeQueryHandler(
    IPositionRepository positionRepository)
    : IQueryHandler<GetPositionsByOrgNodeQuery, Result<IReadOnlyList<PositionResponse>>>
{
    public async Task<Result<IReadOnlyList<PositionResponse>>> HandleAsync(
        GetPositionsByOrgNodeQuery request, CancellationToken ct)
    {
        IReadOnlyList<Position> positions = await positionRepository.GetByOrgNodeAsync(request.OrgNodeId, ct);
        return Result.Success<IReadOnlyList<PositionResponse>>(
            positions.Select(p => new PositionResponse(
                p.Id, p.OrgNodeId, p.Code, p.Title, p.TitleBn,
                p.IsDelegatable, p.IsActive,
                p.Assignments.Select(a => new PositionAssignmentResponse(
                    a.Id, a.UserId, a.EffectiveFrom, a.EffectiveTo,
                    a.IsActive, a.DelegationNote)).ToList())).ToList());
    }
}
