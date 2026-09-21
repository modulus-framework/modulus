using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

namespace Meetup.Modules.Administration.Application.Commands.RejectProposal;

/// <summary>Rejects a proposal (administrator).</summary>
// Single-commit write on one module context: EF's implicit SaveChanges
// transaction plus the transactional outbox suffice. [Transactional] would
// couple Application to the Infrastructure DbContext type.
[SkipTransaction]
public sealed record RejectProposalCommand(Guid ProposalId) : ICommand;
