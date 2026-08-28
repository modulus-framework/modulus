using Modulus.EntityFrameworkCore.Abstractions;
using ProcureFlow.Modules.Identity.Domain.Entities;
using ProcureFlow.Modules.Identity.Domain.Errors;
using ProcureFlow.Modules.Identity.Domain.Repositories;
using ProcureFlow.Modules.Identity.Domain.ValueObjects;
using ProcureFlow.Shared.Domain;
using ProcureFlow.Shared.Domain.ValueObjects;
using Modulus.Mediator.Abstractions.Attributes;
using ProcureFlow.Shared.Application;
using Microsoft.EntityFrameworkCore;

namespace ProcureFlow.Modules.Identity.Application.Roles.Commands;

[RequirePermission(AppPermissions.IdentityAdmin)]
public sealed record RemoveRoleCommand(Guid UserId, Guid RoleId) : Modulus.Mediator.Abstractions.ICommand<Result>;

public sealed class RemoveRoleCommandHandler(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IUnitOfWork unitOfWork)
    : Modulus.Mediator.Abstractions.ICommandHandler<RemoveRoleCommand, Result>
{
    public async Task<Result> HandleAsync(
        RemoveRoleCommand request,
        CancellationToken cancellationToken)
    {
        var userId = UserId.Create(request.UserId);
        var roleId = RoleId.Create(request.RoleId);

        Role? role = await roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure(IdentityErrors.Role.NotFound);
        }

        User? user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(IdentityErrors.User.NotFound);
        }

        // Check if role is already removed - idempotent operation
        if (!user.UserRoles.Any(ur => ur.RoleId == roleId))
        {
            return Result.Success();
        }

        user.RemoveRole(roleId);

        await userRepository.UpdateAsync(user, cancellationToken);

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(Error.Conflict(
                "User.ConcurrencyConflict",
                "User was modified by another operation. Please retry."));
        }

        return Result.Success();
    }
}
