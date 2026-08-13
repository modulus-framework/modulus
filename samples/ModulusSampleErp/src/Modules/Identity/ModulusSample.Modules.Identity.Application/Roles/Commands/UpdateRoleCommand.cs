using Modulus.Mediator.Abstractions.Attributes;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Identity.Application.Roles.Dtos;
using ModulusSample.Modules.Identity.Domain.Entities;
using ModulusSample.Modules.Identity.Domain.Repositories;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Application.Caching;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Application;

namespace ModulusSample.Modules.Identity.Application.Roles.Commands;

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
