using Meetup.Modules.Registrations.Application.IntegrationEvents;
using Meetup.Modules.UserAccess.Domain;
using Modulus.Events.Abstractions;

namespace Meetup.Modules.UserAccess.Application.IntegrationEventHandlers;

public sealed class ActivateUserOnConfirmationHandler(IUserRepository repo, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<UserRegistrationConfirmedIntegrationEvent>
{
    public async Task HandleAsync(UserRegistrationConfirmedIntegrationEvent @event, CancellationToken ct)
    {
        var user = await repo.FindByLoginAsync(@event.Login, ct);
        if (user is null)
        {
            return;
        }

        user.Activate();
        await unitOfWork.CommitAsync(ct);
    }
}
