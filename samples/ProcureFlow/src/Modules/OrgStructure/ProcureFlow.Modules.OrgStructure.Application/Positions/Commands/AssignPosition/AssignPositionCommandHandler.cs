using ProcureFlow.Modules.OrgStructure.Application.Abstractions;
using ProcureFlow.Modules.OrgStructure.Application.Dtos;
using ProcureFlow.Modules.OrgStructure.Domain.Entities;
using ProcureFlow.Modules.OrgStructure.Domain.Errors;
using ProcureFlow.Modules.OrgStructure.Domain.Repositories;
using ProcureFlow.Shared.Domain;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;

namespace ProcureFlow.Modules.OrgStructure.Application.Positions.Commands.AssignPosition;

public sealed record AssignPositionCommand(
    Guid PositionId, Guid UserId, DateOnly EffectiveFrom, DateOnly? EffectiveTo)
    : ICommand<Result<AssignPositionResponse>>;

internal sealed class AssignPositionCommandValidator : AbstractValidator<AssignPositionCommand>
{
    public AssignPositionCommandValidator()
    {
        RuleFor(c => c.PositionId).NotEmpty();
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.EffectiveFrom).NotEmpty();
    }
}

public sealed class AssignPositionCommandHandler(
    IPositionRepository positionRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ILogger<AssignPositionCommandHandler> logger)
    : ICommandHandler<AssignPositionCommand, Result<AssignPositionResponse>>
{
    public async Task<Result<AssignPositionResponse>> HandleAsync(
        AssignPositionCommand request, CancellationToken ct)
    {
        Position? position = await positionRepository.GetByIdAsync(request.PositionId, ct);
        if (position is null)
            return Result.Failure<AssignPositionResponse>(OrgStructureErrors.PositionNotFound(request.PositionId));

        string user = currentUser.UserName ?? "system";
        Result assign = position.Assign(request.UserId, request.EffectiveFrom, request.EffectiveTo, user);
        if (assign.IsFailure)
            return Result.Failure<AssignPositionResponse>(assign.Error);

        await positionRepository.UpdateAsync(position, ct);
        await unitOfWork.CommitAsync(ct);

        var assignment = position.Assignments[^1];
        logger.LogInformation("User {UserId} assigned to Position {PositionId} by {User}",
            request.UserId, request.PositionId, user);
        return Result.Success(new AssignPositionResponse(assignment.Id));
    }
}
