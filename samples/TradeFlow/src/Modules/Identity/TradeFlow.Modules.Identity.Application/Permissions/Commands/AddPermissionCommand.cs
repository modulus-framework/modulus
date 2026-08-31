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
public sealed record AddPermissionCommand(Guid RoleId, string Permission) : Modulus.Mediator.Abstractions.ICommand<Result>;

public sealed class AddPermissionCommandHandler(
    IRoleRepository roleRepository,
    IPermissionRepository permissionRepository,
    IUserRepository userRepository,
    IUserContext userContext,
    ICacheService cacheService,
    IUnitOfWork unitOfWork)
    : Modulus.Mediator.Abstractions.ICommandHandler<AddPermissionCommand, Result>
{
    public async Task<Result> HandleAsync(
        AddPermissionCommand request,
        CancellationToken cancellationToken)
    {
        var roleId = RoleId.Create(request.RoleId);

        Permission? permission = await permissionRepository
            .GetByCodeAsync(request.Permission, cancellationToken);
        if (permission is null)
        {
            return Result.Failure(IdentityErrors.Permission.NotFound);
        }

        UserId grantedByUserId = userContext.UserId;

        Role? role = await roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure(IdentityErrors.Role.NotFound);
        }

        if (role.IsSystem)
        {
            return Result.Failure(IdentityErrors.Permission.CannotModifySystemRole);
        }

        IReadOnlyCollection<string> requestingUserPermissions =
            await userRepository.GetUserPermissionCodesAsync(grantedByUserId, cancellationToken);

        if (!requestingUserPermissions.Contains(request.Permission) &&
            !userContext.IsInRole("Admin"))
        {
            return Result.Failure(IdentityErrors.Permission.CannotGrantPermissionNotHeld);
        }

        role.AddPermission(permission.Id, grantedByUserId);

        await roleRepository.UpdateAsync(role, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        await cacheService.RemoveByPrefixAsync(CacheKeys.User.AllRolesPrefix(), cancellationToken);

        return Result.Success();
    }
}
