using TradeFlow.Modules.OrgStructure.Application.Abstractions;
using TradeFlow.Modules.OrgStructure.Application.Dtos;
using TradeFlow.Modules.OrgStructure.Domain.Entities;
using TradeFlow.Modules.OrgStructure.Domain.Errors;
using TradeFlow.Modules.OrgStructure.Domain.Repositories;
using TradeFlow.Shared.Domain;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;

namespace TradeFlow.Modules.OrgStructure.Application.Positions.Commands.CreatePosition;

public sealed record CreatePositionCommand(
    Guid OrgNodeId, string Code, string Title, string? TitleBn, bool IsDelegatable)
    : ICommand<Result<CreatePositionResponse>>;

internal sealed class CreatePositionCommandValidator : AbstractValidator<CreatePositionCommand>
{
    public CreatePositionCommandValidator()
    {
        RuleFor(c => c.OrgNodeId).NotEmpty();
        RuleFor(c => c.Code).NotEmpty().MaximumLength(50);
        RuleFor(c => c.Title).NotEmpty().MaximumLength(200);
        RuleFor(c => c.TitleBn).MaximumLength(200);
    }
}

public sealed class CreatePositionCommandHandler(
    IOrgNodeRepository orgNodeRepository,
    IPositionRepository positionRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    ILogger<CreatePositionCommandHandler> logger)
    : ICommandHandler<CreatePositionCommand, Result<CreatePositionResponse>>
{
    public async Task<Result<CreatePositionResponse>> HandleAsync(
        CreatePositionCommand request, CancellationToken ct)
    {
        OrgNode? node = await orgNodeRepository.GetByIdAsync(request.OrgNodeId, ct);
        if (node is null)
            return Result.Failure<CreatePositionResponse>(OrgStructureErrors.NotFound(request.OrgNodeId));

        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        if (await positionRepository.ExistsByCodeAsync(tenantId, request.OrgNodeId, request.Code, ct))
            return Result.Failure<CreatePositionResponse>(OrgStructureErrors.DuplicatePositionCode);

        string user = currentUser.UserName ?? "system";
        var createResult = Position.Create(
            Guid.NewGuid(), tenantId, request.OrgNodeId,
            request.Code, request.Title, request.TitleBn, request.IsDelegatable, user);

        if (createResult.IsFailure)
            return Result.Failure<CreatePositionResponse>(createResult.Error);

        await positionRepository.AddAsync(createResult.Value, ct);
        await unitOfWork.CommitAsync(ct);

        logger.LogInformation("Position {PositionId} ({Code}) created at OrgNode {NodeId} by {User}",
            createResult.Value.Id, request.Code, request.OrgNodeId, user);
        return Result.Success(new CreatePositionResponse(createResult.Value.Id));
    }
}
