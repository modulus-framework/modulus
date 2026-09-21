using Meetup.Modules.Administration.Application.Dtos;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Administration.Application.Queries.GetProposals;

public sealed record GetProposalsQuery : IQuery<IReadOnlyList<MeetingGroupProposalDto>>;
