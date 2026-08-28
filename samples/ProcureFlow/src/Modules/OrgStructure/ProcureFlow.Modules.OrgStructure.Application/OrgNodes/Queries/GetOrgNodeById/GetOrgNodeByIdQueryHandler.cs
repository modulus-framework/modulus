using ProcureFlow.Modules.OrgStructure.Application.Abstractions;
using ProcureFlow.Modules.OrgStructure.Application.Dtos;
using ProcureFlow.Modules.OrgStructure.Domain.Entities;
using ProcureFlow.Modules.OrgStructure.Domain.Enums;
using ProcureFlow.Modules.OrgStructure.Domain.Errors;
using ProcureFlow.Modules.OrgStructure.Domain.Repositories;
using ProcureFlow.Shared.Domain;
using FluentValidation;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;

namespace ProcureFlow.Modules.OrgStructure.Application.OrgNodes.Queries.GetOrgNodeById;

public sealed record GetOrgNodeByIdQuery(Guid OrgNodeId)
    : IQuery<Result<OrgNodeDetailResponse>>;

internal sealed class GetOrgNodeByIdQueryValidator : AbstractValidator<GetOrgNodeByIdQuery>
{
    public GetOrgNodeByIdQueryValidator() { RuleFor(c => c.OrgNodeId).NotEmpty(); }
}

public sealed class GetOrgNodeByIdQueryHandler(
    IOrgNodeRepository orgNodeRepository,
    IPositionRepository positionRepository)
    : IQueryHandler<GetOrgNodeByIdQuery, Result<OrgNodeDetailResponse>>
{
    public async Task<Result<OrgNodeDetailResponse>> HandleAsync(GetOrgNodeByIdQuery request, CancellationToken ct)
    {
        OrgNode? node = await orgNodeRepository.GetByIdAsync(request.OrgNodeId, ct);
        if (node is null)
            return Result.Failure<OrgNodeDetailResponse>(OrgStructureErrors.NotFound(request.OrgNodeId));

        IReadOnlyList<OrgNode> children = await orgNodeRepository.GetByParentAsync(node.TenantId, request.OrgNodeId, ct);
        IReadOnlyList<Position> positions = await positionRepository.GetByOrgNodeAsync(request.OrgNodeId, ct);

        return Result.Success(new OrgNodeDetailResponse(
            ToResponse(node),
            children.Select(ToResponse).ToList(),
            positions.Select(ToPositionResponse).ToList()));
    }

    internal static OrgNodeResponse ToResponse(OrgNode node) => new(
        node.Id, node.ParentId, node.NodeType, node.Code,
        node.Name, node.NameBn, node.LtreePath, node.Depth,
        node.EffectiveFrom, node.EffectiveTo, node.IsActive,
        node.CustomsAttributesJson);

    internal static PositionResponse ToPositionResponse(Position p) => new(
        p.Id, p.OrgNodeId, p.Code, p.Title, p.TitleBn,
        p.IsDelegatable, p.IsActive,
        p.Assignments.Select(a => new PositionAssignmentResponse(
            a.Id, a.UserId, a.EffectiveFrom, a.EffectiveTo,
            a.IsActive, a.DelegationNote)).ToList());
}
