using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

namespace Meetup.Modules.Administration.Application.Commands.ProposeMeetingGroup;

/// <summary>Proposes a new meeting group (any member).</summary>
// Single-commit write on one module context: EF's implicit SaveChanges
// transaction plus the transactional outbox suffice. [Transactional] would
// couple Application to the Infrastructure DbContext type.
[SkipTransaction]
public sealed record ProposeMeetingGroupCommand(
    string Name,
    string Description,
    string City,
    string CountryCode,
    string ProposerLogin) : ICommand<Guid>;
