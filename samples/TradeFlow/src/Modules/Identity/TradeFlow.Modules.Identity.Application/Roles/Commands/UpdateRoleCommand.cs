using Modulus.Mediator.Abstractions.Attributes;
using Modulus.EntityFrameworkCore.Abstractions;
using TradeFlow.Modules.Identity.Application.Roles.Dtos;
using TradeFlow.Modules.Identity.Domain.Entities;
using TradeFlow.Modules.Identity.Domain.Repositories;
using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Shared.Application.Caching;
using TradeFlow.Shared.Domain;
using TradeFlow.Shared.Application;

namespace TradeFlow.Modules.Identity.Application.Roles.Commands;

[RequirePermission(AppPermissions.IdentityRoleManageAll)]
public record UpdateRoleCommand(
    Guid RoleId,
    string Name,
    string Description) : Modulus.Mediator.Abstractions.ICommand<Result<RoleDetailResponse>>;

public sealed class UpdateRoleCommandHandler(
    IRoleRepository roleRepository,
    ICacheService cacheService,
    IUnitOfWork unitOfWork)
    : Modulus.Mediator.Abstractions.ICommandHandler<UpdateRoleCommand, Result<RoleDetailResponse>>
{
    public async Task<Result<RoleDetailResponse>> HandleAsync(
        UpdateRoleCommand request,
        CancellationToken cancellationToken)
    {
        var roleId = RoleId.Create(request.RoleId);

        Role? role = await roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure<RoleDetailResponse>(Error.NotFound(
                "Role.NotFound",
                "Role not found."));
        }

        Result updateResult = role.UpdateDetails(request.Name, request.Description);
        if (updateResult.IsFailure)
        {
            return Result.Failure<RoleDetailResponse>(updateResult.Error);
        }

        await roleRepository.UpdateAsync(role, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        await cacheService.RemoveByPrefixAsync(CacheKeys.User.AllRolesPrefix(), cancellationToken);

        var response = new RoleDetailResponse(
            role.Id.Value,
            role.Name,
            role.Description,
            role.IsSystem,
            role.GetActivePermissionCodes().ToList(),
            role.CreatedAtUtc);

        return Result.Success(response);
    }
}
