using Modulus.Mediator.Abstractions.Attributes;
using TradeFlow.Modules.Identity.Application.Abstractions.Authentication;
using Modulus.EntityFrameworkCore.Abstractions;
using TradeFlow.Modules.Identity.Domain.Entities;
using TradeFlow.Modules.Identity.Domain.Errors;
using TradeFlow.Modules.Identity.Domain.Repositories;
using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Shared.Application.Caching;
using TradeFlow.Shared.Domain;
using TradeFlow.Shared.Domain.ValueObjects;
using TradeFlow.Shared.Application;
using UserId = TradeFlow.Modules.Identity.Domain.ValueObjects.UserId;

namespace TradeFlow.Modules.Identity.Application.Permissions.Commands;

[RequirePermission(AppPermissions.IdentityRoleManageAll)]
public sealed record RemovePermissionCommand(Guid RoleId, string Permission) : Modulus.Mediator.Abstractions.ICommand<Result>;

public sealed class RemovePermissionCommandHandler(
    IRoleRepository roleRepository,
    IPermissionRepository permissionRepository,
    IUserContext userContext,
    ICacheService cacheService,
    IUnitOfWork unitOfWork)
    : Modulus.Mediator.Abstractions.ICommandHandler<RemovePermissionCommand, Result>
{
    public async Task<Result> HandleAsync(
        RemovePermissionCommand request,
        CancellationToken cancellationToken)
    {
        var roleId = RoleId.Create(request.RoleId);

        Permission? permission = await permissionRepository
            .GetByCodeAsync(request.Permission, cancellationToken);
        if (permission is null)
        {
            return Result.Failure(IdentityErrors.Permission.NotFound);
        }

        UserId revokedByUserId = userContext.UserId;

        Role? role = await roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure(IdentityErrors.Role.NotFound);
        }

        role.RemovePermission(permission.Id, revokedByUserId);

        await roleRepository.UpdateAsync(role, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        await cacheService.RemoveByPrefixAsync(CacheKeys.User.AllRolesPrefix(), cancellationToken);

        return Result.Success();
    }
}
