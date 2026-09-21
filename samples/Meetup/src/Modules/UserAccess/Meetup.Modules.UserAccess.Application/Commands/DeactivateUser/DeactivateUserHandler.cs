using Meetup.Modules.UserAccess.Domain;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.UserAccess.Application.Commands.DeactivateUser;

public sealed class DeactivateUserHandler(IUserRepository repo, IUnitOfWork unitOfWork)
    : ICommandHandler<DeactivateUserCommand, Modulus.Core.Abstractions.Common.Unit>
{
    public async Task<Modulus.Core.Abstractions.Common.Unit> HandleAsync(
        DeactivateUserCommand command, CancellationToken ct)
    {
        var user = await repo.GetByIdAsync(command.UserId, ct)
            ?? throw new KeyNotFoundException($"User {command.UserId} not found.");

        user.Deactivate();
        await unitOfWork.CommitAsync(ct);
        return new Modulus.Core.Abstractions.Common.Unit();
    }
}
