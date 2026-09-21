using Meetup.Modules.Administration.Domain;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Administration.Application.Commands.ProposeMeetingGroup;

public sealed class ProposeMeetingGroupHandler(
    IMeetingGroupProposalRepository repo, IUnitOfWork unitOfWork)
    : ICommandHandler<ProposeMeetingGroupCommand, Guid>
{
    public async Task<Guid> HandleAsync(ProposeMeetingGroupCommand command, CancellationToken ct)
    {
        var proposal = MeetingGroupProposal.ProposeNew(
            command.Name, command.Description, command.City, command.CountryCode, command.ProposerLogin);

        await repo.AddAsync(proposal, ct);
        await unitOfWork.CommitAsync(ct);
        return proposal.Id;
    }
}
