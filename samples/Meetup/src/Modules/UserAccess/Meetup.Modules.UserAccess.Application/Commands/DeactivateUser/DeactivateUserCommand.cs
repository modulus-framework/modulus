using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

namespace Meetup.Modules.UserAccess.Application.Commands.DeactivateUser;

/// <summary>Deactivates a user (blocks authentication).</summary>
// Single-commit write on one module context: EF's implicit SaveChanges
// transaction plus the transactional outbox suffice. [Transactional] would
// couple Application to the Infrastructure DbContext type.
[SkipTransaction]
public sealed record DeactivateUserCommand(Guid UserId) : ICommand;
