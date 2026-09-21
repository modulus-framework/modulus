using Meetup.Modules.Administration.Application.IntegrationEvents;
using Meetup.Modules.Administration.Domain;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Administration.Application.Commands.AcceptProposal;

public sealed class AcceptProposalHandler(
    IMeetingGroupProposalRepository repo, IUnitOfWork unitOfWork)
    : ICommandHandler<AcceptProposalCommand, Modulus.Core.Abstractions.Common.Unit>
{
    public async Task<Modulus.Core.Abstractions.Common.Unit> HandleAsync(
        AcceptProposalCommand command, CancellationToken ct)
    {
        var proposal = await repo.GetByIdAsync(command.ProposalId, ct)
            ?? throw new KeyNotFoundException($"Proposal {command.ProposalId} not found.");

        proposal.Accept();

        // Transactional outbox: attach BEFORE CommitAsync so ModuleDbContext
        // writes the outbox row in the same DB transaction as the aggregate.
        proposal.AddIntegrationEvent(new MeetingGroupProposalAcceptedIntegrationEvent(
            proposal.Id, proposal.Name, proposal.Description,
            proposal.City, proposal.CountryCode, proposal.ProposerLogin));

        await unitOfWork.CommitAsync(ct);

        return new Modulus.Core.Abstractions.Common.Unit();
    }
}
