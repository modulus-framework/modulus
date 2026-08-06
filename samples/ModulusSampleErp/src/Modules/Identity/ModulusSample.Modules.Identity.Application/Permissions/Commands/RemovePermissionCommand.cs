using Modulus.Mediator.Abstractions.Attributes;
using ModulusSample.Modules.Identity.Application.Abstractions.Authentication;
using ModulusSample.Modules.Identity.Application.Abstractions.Data;
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
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cacheService.RemoveByPrefixAsync(CacheKeys.User.AllRolesPrefix(), cancellationToken);

        return Result.Success();
    }
}
