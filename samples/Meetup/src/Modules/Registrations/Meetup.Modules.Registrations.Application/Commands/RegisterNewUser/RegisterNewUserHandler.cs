using Meetup.Modules.Registrations.Application.IntegrationEvents;
using Meetup.Modules.Registrations.Domain;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Registrations.Application.Commands.RegisterNewUser;

public sealed class RegisterNewUserHandler(
    IUserRegistrationRepository repo,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RegisterNewUserCommand, Guid>
{
    public async Task<Guid> HandleAsync(RegisterNewUserCommand command, CancellationToken ct)
    {
        var existing = await repo.FindByLoginAsync(command.Login, ct);
        if (existing is not null)
        {
            throw new InvalidOperationException($"Login '{command.Login}' is already taken.");
        }

        // MVP: SHA-256 hash stands in for ASP.NET Identity's PasswordHasher.
        // Production: use PasswordHasher<TUser> (salted, iterated).
        var passwordHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(command.Password)));

        var registration = UserRegistration.RegisterNewUser(
            command.Login, command.Email, passwordHash, command.FirstName, command.LastName);

        // Transactional outbox: attach BEFORE CommitAsync so ModuleDbContext
        // writes the outbox row in the same DB transaction as the aggregate.
        // The OutboxProcessor relays it to handlers (inbox-deduped).
        registration.AddIntegrationEvent(new UserRegisteredIntegrationEvent(
            registration.Id, registration.Login, registration.Email));

        await repo.AddAsync(registration, ct);
        await unitOfWork.CommitAsync(ct);

        return registration.Id;
    }
}
