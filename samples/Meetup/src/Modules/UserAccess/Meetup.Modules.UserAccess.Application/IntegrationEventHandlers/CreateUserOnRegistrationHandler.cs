using Meetup.Modules.Registrations.Application.IntegrationEvents;
using Meetup.Modules.UserAccess.Domain;
using Modulus.Events.Abstractions;

namespace Meetup.Modules.UserAccess.Application.IntegrationEventHandlers;

/// <summary>
/// Cross-module integration (kgrzybek: async integration events only —
/// modules never call each other directly).
/// </summary>
public sealed class CreateUserOnRegistrationHandler(IUserRepository repo, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<UserRegisteredIntegrationEvent>
{
    public async Task HandleAsync(UserRegisteredIntegrationEvent @event, CancellationToken ct)
    {
        var exists = await repo.FindByLoginAsync(@event.Login, ct);
        if (exists is not null)
        {
            return;
        }

        // Password hash travels out-of-band here: Registrations owns credentials
        // until confirmation; the MVP stores an empty hash and UserAccess trusts
        // the Confirmed event. Production: pass a one-time activation token.
        await repo.AddAsync(User.Create(@event.Login, @event.Email, string.Empty), ct);
        await unitOfWork.CommitAsync(ct);
    }
}
