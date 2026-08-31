using TradeFlow.Modules.Identity.Application.Abstractions.Authentication;
using Modulus.EntityFrameworkCore.Abstractions;
using TradeFlow.Modules.Identity.Application.Abstractions.Identity;
using TradeFlow.Modules.Identity.Domain.Entities;
using TradeFlow.Modules.Identity.Domain.Errors;
using TradeFlow.Modules.Identity.Domain.Repositories;
using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Shared.Domain;
using TradeFlow.Shared.Domain.ValueObjects;

namespace TradeFlow.Modules.Identity.Application.Users.Commands;

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
