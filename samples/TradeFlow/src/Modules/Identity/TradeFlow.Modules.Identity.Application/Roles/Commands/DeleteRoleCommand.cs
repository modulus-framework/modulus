using Modulus.Mediator.Abstractions.Attributes;
using Modulus.EntityFrameworkCore.Abstractions;
using TradeFlow.Modules.Identity.Domain.Entities;
using TradeFlow.Modules.Identity.Domain.Errors;
using TradeFlow.Modules.Identity.Domain.Repositories;
using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Shared.Application.Caching;
using TradeFlow.Shared.Domain;
using TradeFlow.Shared.Application;

namespace TradeFlow.Modules.Identity.Application.Roles.Commands;

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
