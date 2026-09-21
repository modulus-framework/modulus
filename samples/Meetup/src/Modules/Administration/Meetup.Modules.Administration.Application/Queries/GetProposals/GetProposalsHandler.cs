using Meetup.Modules.Administration.Application.Dtos;
using Meetup.Modules.Administration.Domain;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Administration.Application.Queries.GetProposals;

public sealed class GetProposalsHandler(IMeetingGroupProposalRepository repo)
    : IQueryHandler<GetProposalsQuery, IReadOnlyList<MeetingGroupProposalDto>>
{
    public async Task<IReadOnlyList<MeetingGroupProposalDto>> HandleAsync(
        GetProposalsQuery query, CancellationToken ct)
    {
        var items = await repo.GetAllAsync(ct);
        return items
            .Select(x => new MeetingGroupProposalDto(
                x.Id, x.Name, x.Description, x.City, x.CountryCode,
                x.ProposerLogin, x.Status.ToString(), x.ProposedAt, x.DecidedAt))
            .ToList();
    }
}
