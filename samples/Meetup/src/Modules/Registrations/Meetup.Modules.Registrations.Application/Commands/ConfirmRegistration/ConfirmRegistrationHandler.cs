using Meetup.Modules.Registrations.Application.IntegrationEvents;
using Meetup.Modules.Registrations.Domain;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Registrations.Application.Commands.ConfirmRegistration;

public sealed class ConfirmRegistrationHandler(
    IUserRegistrationRepository repo,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ConfirmRegistrationCommand, Modulus.Core.Abstractions.Common.Unit>
{
    public async Task<Modulus.Core.Abstractions.Common.Unit> HandleAsync(
        ConfirmRegistrationCommand command, CancellationToken ct)
    {
        var registration = await repo.GetByIdAsync(command.RegistrationId, ct)
            ?? throw new KeyNotFoundException($"Registration {command.RegistrationId} not found.");

        registration.Confirm();

        // Transactional outbox: attach BEFORE CommitAsync so ModuleDbContext
        // writes the outbox row in the same DB transaction as the aggregate.
        registration.AddIntegrationEvent(new UserRegistrationConfirmedIntegrationEvent(
            registration.Id, registration.Login));

        await unitOfWork.CommitAsync(ct);

        return new Modulus.Core.Abstractions.Common.Unit();
    }
}
