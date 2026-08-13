using ModulusSample.Modules.Identity.Application.Abstractions.Authentication;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Identity.Application.Abstractions.Identity;
using ModulusSample.Modules.Identity.Domain.Entities;
using ModulusSample.Modules.Identity.Domain.Errors;
using ModulusSample.Modules.Identity.Domain.Repositories;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Domain.ValueObjects;

namespace ModulusSample.Modules.Identity.Application.Users.Commands;

internal sealed class ChangePasswordCommandHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    IUserContext userContext)
    : Modulus.Mediator.Abstractions.ICommandHandler<ChangePasswordCommand, Result>
{
    public async Task<Result> HandleAsync(
        ChangePasswordCommand request,
        CancellationToken cancellationToken)
    {
        User? user = await userRepository.GetByIdAsync(
            new UserId(userContext.UserId),
            cancellationToken);

        if (user is null || userContext.UserId == Guid.Empty)
        {
            return Result.Failure(IdentityErrors.User.InvalidCredentials);
        }

        if (user.PasswordHash != null && !passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return Result.Failure(IdentityErrors.User.InvalidCredentials);
        }

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result.Success();
    }
}
