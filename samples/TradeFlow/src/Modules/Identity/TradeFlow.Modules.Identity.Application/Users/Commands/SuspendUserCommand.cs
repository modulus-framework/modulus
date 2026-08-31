using Modulus.EntityFrameworkCore.Abstractions;
using TradeFlow.Modules.Identity.Domain.Entities;
using TradeFlow.Modules.Identity.Domain.Errors;
using TradeFlow.Modules.Identity.Domain.Repositories;
using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Shared.Domain;
using TradeFlow.Shared.Domain.ValueObjects;
using TradeFlow.Shared.Application;

namespace TradeFlow.Modules.Identity.Application.Users.Commands;

public sealed record SuspendUserCommand(Guid UserId, string Reason) : Modulus.Mediator.Abstractions.ICommand<Result>;

public sealed class SuspendUserCommandHandler(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork)
    : Modulus.Mediator.Abstractions.ICommandHandler<SuspendUserCommand, Result>
{
    public async Task<Result> HandleAsync(
        SuspendUserCommand request,
        CancellationToken cancellationToken)
    {
        var userId = UserId.Create(request.UserId);

        User? user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(IdentityErrors.User.NotFound);
        }

        user.Suspend(request.Reason);

        await userRepository.UpdateAsync(user, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return Result.Success();
    }
}
