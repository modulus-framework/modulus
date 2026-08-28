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

namespace ProcureFlow.Modules.OrgStructure.Application.OrgNodes.Commands.UpdateOrgNode;

public sealed record UpdateOrgNodeCommand(
    Guid OrgNodeId, string Name, string? NameBn,
    DateOnly? EffectiveTo, string? CustomsAttributesJson)
    : ICommand<Result<UpdateOrgNodeResponse>>;

internal sealed class UpdateOrgNodeCommandValidator : AbstractValidator<UpdateOrgNodeCommand>
{
    public UpdateOrgNodeCommandValidator()
    {
        RuleFor(c => c.OrgNodeId).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.NameBn).MaximumLength(200);
        RuleFor(c => c.CustomsAttributesJson).MaximumLength(4000);
    }
}

public sealed class UpdateOrgNodeCommandHandler(
    IOrgNodeRepository orgNodeRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ILogger<UpdateOrgNodeCommandHandler> logger)
    : ICommandHandler<UpdateOrgNodeCommand, Result<UpdateOrgNodeResponse>>
{
    public async Task<Result<UpdateOrgNodeResponse>> HandleAsync(
        UpdateOrgNodeCommand request, CancellationToken ct)
    {
        OrgNode? node = await orgNodeRepository.GetByIdAsync(request.OrgNodeId, ct);
        if (node is null)
            return Result.Failure<UpdateOrgNodeResponse>(OrgStructureErrors.NotFound(request.OrgNodeId));

        string user = currentUser.UserName ?? "system";
        Result update = node.Update(request.Name, request.NameBn, request.EffectiveTo, request.CustomsAttributesJson, user);
        if (update.IsFailure)
            return Result.Failure<UpdateOrgNodeResponse>(update.Error);

        await orgNodeRepository.UpdateAsync(node, ct);
        await unitOfWork.CommitAsync(ct);

        logger.LogInformation("OrgNode {NodeId} updated by {User}", request.OrgNodeId, user);
        return Result.Success(new UpdateOrgNodeResponse(node.Id));
    }
}
