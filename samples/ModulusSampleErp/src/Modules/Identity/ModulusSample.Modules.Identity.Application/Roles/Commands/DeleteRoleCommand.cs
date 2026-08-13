using Modulus.Mediator.Abstractions.Attributes;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Identity.Domain.Entities;
using ModulusSample.Modules.Identity.Domain.Errors;
using ModulusSample.Modules.Identity.Domain.Repositories;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Application.Caching;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Application;

namespace ModulusSample.Modules.Identity.Application.Roles.Commands;

[RequirePermission(AppPermissions.IdentityRoleManageAll)]
public sealed record DeleteRoleCommand(Guid RoleId) : Modulus.Mediator.Abstractions.ICommand<Result>;

public sealed class DeleteRoleCommandHandler(
    IRoleRepository roleRepository,
    ICacheService cacheService,
    IUnitOfWork unitOfWork)
    : Modulus.Mediator.Abstractions.ICommandHandler<DeleteRoleCommand, Result>
{
    public async Task<Result> HandleAsync(
        DeleteRoleCommand request,
        CancellationToken cancellationToken)
    {
        var roleId = RoleId.Create(request.RoleId);

        Role? role = await roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure(IdentityErrors.Role.NotFound);
        }

        if (role.IsSystem)
        {
            return Result.Failure(IdentityErrors.Role.CannotDeleteSystemRole);
        }

        await roleRepository.DeleteAsync(role, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        await cacheService.RemoveByPrefixAsync(CacheKeys.User.AllRolesPrefix(), cancellationToken);

        return Result.Success();
    }
}
