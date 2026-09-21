using Meetup.Modules.Administration.Domain;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Administration.Application.Commands.RejectProposal;

public sealed class RejectProposalHandler(
    IMeetingGroupProposalRepository repo, IUnitOfWork unitOfWork)
    : ICommandHandler<RejectProposalCommand, Modulus.Core.Abstractions.Common.Unit>
{
    public async Task<Modulus.Core.Abstractions.Common.Unit> HandleAsync(
        RejectProposalCommand command, CancellationToken ct)
    {
        var proposal = await repo.GetByIdAsync(command.ProposalId, ct)
            ?? throw new KeyNotFoundException($"Proposal {command.ProposalId} not found.");

        proposal.Reject();
        await unitOfWork.CommitAsync(ct);
        return new Modulus.Core.Abstractions.Common.Unit();
    }
}
