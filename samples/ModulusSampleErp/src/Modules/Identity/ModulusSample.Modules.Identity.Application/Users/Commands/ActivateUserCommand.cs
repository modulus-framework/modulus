using ModulusSample.Modules.Identity.Application.Abstractions.Data;
using ModulusSample.Modules.Identity.Domain.Entities;
using ModulusSample.Modules.Identity.Domain.Errors;
using ModulusSample.Modules.Identity.Domain.Repositories;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Domain.ValueObjects;
using Modulus.Mediator.Abstractions.Attributes;
using ModulusSample.Shared.Application;

namespace ModulusSample.Modules.Identity.Application.Users.Commands;

[RequirePermission(AppPermissions.IdentityAdmin)]
public sealed record ActivateUserCommand(Guid UserId) : Modulus.Mediator.Abstractions.ICommand<Result>;

public sealed class ActivateUserCommandHandler(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork)
    : Modulus.Mediator.Abstractions.ICommandHandler<ActivateUserCommand, Result>
{
    public async Task<Result> HandleAsync(
        ActivateUserCommand request,
        CancellationToken cancellationToken)
    {
        var userId = UserId.Create(request.UserId);

        User? user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(IdentityErrors.User.NotFound);
        }

        user.Activate();

        await userRepository.UpdateAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
