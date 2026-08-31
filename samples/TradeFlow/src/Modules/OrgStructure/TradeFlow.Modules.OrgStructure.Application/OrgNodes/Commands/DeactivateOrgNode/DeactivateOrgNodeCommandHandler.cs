using TradeFlow.Modules.OrgStructure.Application.Abstractions;
using TradeFlow.Modules.OrgStructure.Domain.Entities;
using TradeFlow.Modules.OrgStructure.Domain.Errors;
using TradeFlow.Modules.OrgStructure.Domain.Repositories;
using TradeFlow.Shared.Domain;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;

namespace TradeFlow.Modules.OrgStructure.Application.OrgNodes.Commands.DeactivateOrgNode;

public sealed record DeactivateOrgNodeCommand(Guid OrgNodeId)
    : ICommand<Result>;

internal sealed class DeactivateOrgNodeCommandValidator : AbstractValidator<DeactivateOrgNodeCommand>
{
    public DeactivateOrgNodeCommandValidator() { RuleFor(c => c.OrgNodeId).NotEmpty(); }
}

public sealed class DeactivateOrgNodeCommandHandler(
    IOrgNodeRepository orgNodeRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ILogger<DeactivateOrgNodeCommandHandler> logger)
    : ICommandHandler<DeactivateOrgNodeCommand, Result>
{
    public async Task<Result> HandleAsync(DeactivateOrgNodeCommand request, CancellationToken ct)
    {
        OrgNode? node = await orgNodeRepository.GetByIdAsync(request.OrgNodeId, ct);
        if (node is null)
            return Result.Failure(OrgStructureErrors.NotFound(request.OrgNodeId));

        IReadOnlyList<OrgNode> children = await orgNodeRepository.GetByParentAsync(node.TenantId, request.OrgNodeId, ct);
        if (children.Any(c => c.IsActive))
            return Result.Failure(OrgStructureErrors.NodeHasChildren(request.OrgNodeId));

        string user = currentUser.UserName ?? "system";
        Result deactivate = node.Deactivate(user);
        if (deactivate.IsFailure) return deactivate;

        await orgNodeRepository.UpdateAsync(node, ct);
        await unitOfWork.CommitAsync(ct);

        logger.LogInformation("OrgNode {NodeId} deactivated by {User}", request.OrgNodeId, user);
        return Result.Success();
    }
}
