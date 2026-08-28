using Modulus.EntityFrameworkCore.Abstractions;
using ProcureFlow.Modules.Identity.Domain.Entities;
using ProcureFlow.Modules.Identity.Domain.Enums;
using ProcureFlow.Modules.Identity.Domain.Errors;
using ProcureFlow.Modules.Identity.Domain.Repositories;
using ProcureFlow.Modules.Identity.Domain.ValueObjects;
using ProcureFlow.Shared.Domain;
using ProcureFlow.Shared.Domain.ValueObjects;
using Modulus.Mediator.Abstractions.Attributes;
using ProcureFlow.Shared.Application;

namespace ProcureFlow.Modules.Identity.Application.Users.Commands;

[RequirePermission(AppPermissions.IdentityUserManageAll)]
public sealed record UpdateUserTypeCommand(Guid UserId, UserType UserType) : Modulus.Mediator.Abstractions.ICommand<Result>;

public sealed class UpdateUserTypeCommandHandler(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork)
    : Modulus.Mediator.Abstractions.ICommandHandler<UpdateUserTypeCommand, Result>
{
    public async Task<Result> HandleAsync(
        UpdateUserTypeCommand request,
        CancellationToken cancellationToken)
    {
        var userId = UserId.Create(request.UserId);

        User? user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(IdentityErrors.User.NotFound);
        }

        user.UpdateUserType(request.UserType);

        await userRepository.UpdateAsync(user, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return Result.Success();
    }
}
