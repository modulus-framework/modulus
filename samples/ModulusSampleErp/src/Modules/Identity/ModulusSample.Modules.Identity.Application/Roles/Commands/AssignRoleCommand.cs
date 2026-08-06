using ModulusSample.Modules.Identity.Application.Abstractions.Data;
using ModulusSample.Modules.Identity.Domain.Entities;
using ModulusSample.Modules.Identity.Domain.Errors;
using ModulusSample.Modules.Identity.Domain.Repositories;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Domain.ValueObjects;
using Modulus.Mediator.Abstractions.Attributes;
using ModulusSample.Shared.Application;
using Microsoft.EntityFrameworkCore;
using DbUpdateConcurrencyException = Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException;

namespace ModulusSample.Modules.Identity.Application.Roles.Commands;

[RequirePermission(AppPermissions.IdentityAdmin)]
public sealed record AssignRoleCommand(Guid UserId, Guid RoleId) : Modulus.Mediator.Abstractions.ICommand<Result>;

public sealed class AssignRoleCommandHandler(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IUnitOfWork unitOfWork)
    : Modulus.Mediator.Abstractions.ICommandHandler<AssignRoleCommand, Result>
{
    public async Task<Result> HandleAsync(
        AssignRoleCommand request,
        CancellationToken cancellationToken)
    {
        var userId = UserId.Create(request.UserId);
        var roleId = RoleId.Create(request.RoleId);

        Role? role = await roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure(IdentityErrors.Role.NotFound);
        }

        User? user = await userRepository.GetByIdWithRolesAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(IdentityErrors.User.NotFound);
        }

        if (user.UserRoles.Any(ur => ur.RoleId == roleId))
        {
            return Result.Success();
        }

        user.AddRole(roleId);

        await userRepository.UpdateAsync(user, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(Error.Conflict(
                "User.ConcurrencyConflict",
                "The user was modified by another operation. Please retry."));
        }
        catch (DbUpdateException ex)
        {
            return Result.Failure(Error.Failure(
                "User.DatabaseError",
                ex.Message));
        }

        return Result.Success();
    }
}
