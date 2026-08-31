using TradeFlow.Modules.OrgStructure.Application.Abstractions;
using TradeFlow.Modules.OrgStructure.Application.Dtos;
using TradeFlow.Modules.OrgStructure.Domain.Entities;
using TradeFlow.Modules.OrgStructure.Domain.Enums;
using TradeFlow.Modules.OrgStructure.Domain.Errors;
using TradeFlow.Modules.OrgStructure.Domain.Repositories;
using TradeFlow.Shared.Domain;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;

namespace TradeFlow.Modules.OrgStructure.Application.OrgNodes.Commands.CreateOrgNode;

public sealed record CreateOrgNodeCommand(
    Guid? ParentId, OrgNodeType NodeType, string Code, string Name,
    string? NameBn, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    string? CustomsAttributesJson)
    : ICommand<Result<CreateOrgNodeResponse>>;

internal sealed class CreateOrgNodeCommandValidator : AbstractValidator<CreateOrgNodeCommand>
{
    public CreateOrgNodeCommandValidator()
    {
        RuleFor(c => c.NodeType).IsInEnum();
        RuleFor(c => c.Code).NotEmpty().MaximumLength(50);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.NameBn).MaximumLength(200);
        RuleFor(c => c.EffectiveFrom).NotEmpty();
        RuleFor(c => c.CustomsAttributesJson).MaximumLength(4000);
    }
}

public sealed class CreateOrgNodeCommandHandler(
    IOrgNodeRepository orgNodeRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    ILogger<CreateOrgNodeCommandHandler> logger)
    : ICommandHandler<CreateOrgNodeCommand, Result<CreateOrgNodeResponse>>
{
    public async Task<Result<CreateOrgNodeResponse>> HandleAsync(
        CreateOrgNodeCommand request, CancellationToken ct)
    {
        string user = currentUser.UserName ?? "system";
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;

        if (await orgNodeRepository.ExistsByCodeAsync(tenantId, request.Code, ct))
            return Result.Failure<CreateOrgNodeResponse>(OrgStructureErrors.DuplicateCode);

        string parentLtreePath = "";
        int parentDepth = -1;

        if (request.ParentId.HasValue)
        {
            OrgNode? parent = await orgNodeRepository.GetByIdAsync(request.ParentId.Value, ct);
            if (parent is null)
                return Result.Failure<CreateOrgNodeResponse>(OrgStructureErrors.NotFound(request.ParentId.Value));
            parentLtreePath = parent.LtreePath;
            parentDepth = parent.Depth;
        }

        var createResult = OrgNode.Create(
            Guid.NewGuid(), tenantId, request.ParentId, request.NodeType,
            request.Code, request.Name, request.NameBn,
            request.EffectiveFrom, request.EffectiveTo,
            request.CustomsAttributesJson, user,
            parentLtreePath, parentDepth);

        if (createResult.IsFailure)
            return Result.Failure<CreateOrgNodeResponse>(createResult.Error);

        await orgNodeRepository.AddAsync(createResult.Value, ct);
        await unitOfWork.CommitAsync(ct);

        logger.LogInformation("OrgNode {NodeId} ({Code}) created by {User}", createResult.Value.Id, request.Code, user);
        return Result.Success(new CreateOrgNodeResponse(createResult.Value.Id));
    }
}
