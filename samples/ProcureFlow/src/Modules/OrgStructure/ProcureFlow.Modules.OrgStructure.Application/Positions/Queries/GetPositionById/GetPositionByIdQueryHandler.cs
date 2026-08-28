using ProcureFlow.Modules.OrgStructure.Application.Abstractions;
using ProcureFlow.Modules.OrgStructure.Application.Dtos;
using ProcureFlow.Modules.OrgStructure.Domain.Entities;
using ProcureFlow.Modules.OrgStructure.Domain.Errors;
using ProcureFlow.Modules.OrgStructure.Domain.Repositories;
using ProcureFlow.Shared.Domain;
using FluentValidation;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;

namespace ProcureFlow.Modules.OrgStructure.Application.Positions.Queries.GetPositionById;

public sealed record GetPositionByIdQuery(Guid PositionId)
    : IQuery<Result<PositionResponse>>;

internal sealed class GetPositionByIdQueryValidator : AbstractValidator<GetPositionByIdQuery>
{
    public GetPositionByIdQueryValidator() { RuleFor(c => c.PositionId).NotEmpty(); }
}

public sealed class GetPositionByIdQueryHandler(
    IPositionRepository positionRepository)
    : IQueryHandler<GetPositionByIdQuery, Result<PositionResponse>>
{
    public async Task<Result<PositionResponse>> HandleAsync(GetPositionByIdQuery request, CancellationToken ct)
    {
        Position? position = await positionRepository.GetByIdAsync(request.PositionId, ct);
        if (position is null)
            return Result.Failure<PositionResponse>(OrgStructureErrors.PositionNotFound(request.PositionId));

        return Result.Success(new PositionResponse(
            position.Id, position.OrgNodeId, position.Code, position.Title,
            position.TitleBn, position.IsDelegatable, position.IsActive,
            position.Assignments.Select(a => new PositionAssignmentResponse(
                a.Id, a.UserId, a.EffectiveFrom, a.EffectiveTo,
                a.IsActive, a.DelegationNote)).ToList()));
    }
}
