using Modulus.Mediator.Abstractions.Attributes;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Identity.Application.Roles.Dtos;
using ModulusSample.Modules.Identity.Domain.Entities;
using ModulusSample.Modules.Identity.Domain.Errors;
using ModulusSample.Modules.Identity.Domain.Repositories;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Application.Caching;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Application;

namespace ModulusSample.Modules.Identity.Application.Roles.Commands;

[RequirePermission(AppPermissions.IdentityRoleManageAll)]
public sealed record CreateRoleCommand(
    string Name,
    string Description) : Modulus.Mediator.Abstractions.ICommand<Result<CreateRoleResponse>>;

public sealed class CreateRoleCommandHandler(
    IRoleRepository roleRepository,
    ICacheService cacheService,
    IUnitOfWork unitOfWork)
    : Modulus.Mediator.Abstractions.ICommandHandler<CreateRoleCommand, Result<CreateRoleResponse>>
{
    public async Task<Result<CreateRoleResponse>> HandleAsync(
        CreateRoleCommand request,
        CancellationToken cancellationToken)
    {
        if (await roleRepository.ExistsByNameAsync(request.Name, cancellationToken))
        {
            return Result.Failure<CreateRoleResponse>(IdentityErrors.Role.DuplicateName);
        }

        Result<Role> role = Role.Create(
            RoleId.Create(Guid.NewGuid()),
            request.Name,
            request.Description,
            isSystem: false);

        await roleRepository.AddAsync(role.Value, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        await cacheService.RemoveByPrefixAsync(CacheKeys.User.AllRolesPrefix(), cancellationToken);

        return Result.Success(new CreateRoleResponse(role.Value.Id.Value, role.Value.Name));
    }
}
