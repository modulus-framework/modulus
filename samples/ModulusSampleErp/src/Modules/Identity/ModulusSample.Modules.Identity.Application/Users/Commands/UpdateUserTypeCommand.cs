using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Identity.Domain.Entities;
using ModulusSample.Modules.Identity.Domain.Enums;
using ModulusSample.Modules.Identity.Domain.Errors;
using ModulusSample.Modules.Identity.Domain.Repositories;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Domain.ValueObjects;
using Modulus.Mediator.Abstractions.Attributes;
using ModulusSample.Shared.Application;

namespace ModulusSample.Modules.Identity.Application.Users.Commands;

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
