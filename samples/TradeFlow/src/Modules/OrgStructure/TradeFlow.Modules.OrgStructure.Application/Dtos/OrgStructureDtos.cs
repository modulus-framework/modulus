using TradeFlow.Modules.OrgStructure.Domain.Entities;
using TradeFlow.Modules.OrgStructure.Domain.Enums;

namespace TradeFlow.Modules.OrgStructure.Application.Dtos;

public sealed record OrgNodeResponse(
    Guid Id, Guid? ParentId, OrgNodeType NodeType, string Code,
    string Name, string? NameBn, string LtreePath, int Depth,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive,
    string? CustomsAttributesJson);

public sealed record OrgNodeDetailResponse(
    OrgNodeResponse Node,
    IReadOnlyList<OrgNodeResponse> Children,
    IReadOnlyList<PositionResponse> Positions);

public sealed record OrgTreeResponse(
    IReadOnlyList<OrgNodeResponse> Nodes);

public sealed record CreateOrgNodeResponse(Guid OrgNodeId);

public sealed record UpdateOrgNodeResponse(Guid OrgNodeId);

public sealed record PositionResponse(
    Guid Id, Guid OrgNodeId, string Code, string Title, string? TitleBn,
    bool IsDelegatable, bool IsActive,
    IReadOnlyList<PositionAssignmentResponse> Assignments);

public sealed record PositionAssignmentResponse(
    Guid Id, Guid UserId, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    bool IsActive, string? DelegationNote);

public sealed record CreatePositionResponse(Guid PositionId);

public sealed record AssignPositionResponse(Guid AssignmentId);
