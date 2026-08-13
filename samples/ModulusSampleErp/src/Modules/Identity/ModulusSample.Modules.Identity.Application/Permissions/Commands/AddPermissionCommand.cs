using Modulus.Mediator.Abstractions.Attributes;
using ModulusSample.Modules.Identity.Application.Abstractions.Authentication;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Identity.Domain.Entities;
using ModulusSample.Modules.Identity.Domain.Errors;
using ModulusSample.Modules.Identity.Domain.Repositories;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Application.Caching;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Domain.ValueObjects;
using ModulusSample.Shared.Application;
using UserId = ModulusSample.Modules.Identity.Domain.ValueObjects.UserId;

namespace ModulusSample.Modules.Identity.Application.Permissions.Commands;

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
