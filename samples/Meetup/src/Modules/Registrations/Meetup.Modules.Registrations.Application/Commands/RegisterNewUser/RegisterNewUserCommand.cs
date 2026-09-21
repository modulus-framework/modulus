using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

namespace Meetup.Modules.Registrations.Application.Commands.RegisterNewUser;

/// <summary>Registers a new user (creates a pending UserRegistration).</summary>
// Single-commit write on one module context: EF's implicit SaveChanges
// transaction plus the transactional outbox suffice. [Transactional] would
// couple Application to the Infrastructure DbContext type.
[SkipTransaction]
public sealed record RegisterNewUserCommand(
    string Login,
    string Email,
    string Password,
    string FirstName,
    string LastName) : ICommand<Guid>;
