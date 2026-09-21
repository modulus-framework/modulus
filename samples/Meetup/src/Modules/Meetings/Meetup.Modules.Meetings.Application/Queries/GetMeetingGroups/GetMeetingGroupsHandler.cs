using Meetup.Modules.Meetings.Application.Dtos;
using Meetup.Modules.Meetings.Domain.Repositories;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Meetings.Application.Queries.GetMeetingGroups;

public sealed class GetMeetingGroupsHandler(IMeetingGroupRepository groups)
    : IQueryHandler<GetMeetingGroupsQuery, IReadOnlyList<MeetingGroupDto>>
{
    public async Task<IReadOnlyList<MeetingGroupDto>> HandleAsync(GetMeetingGroupsQuery query, CancellationToken ct)
    {
        var items = await groups.GetAllAsync(ct);
        return items
            .Select(x => new MeetingGroupDto(
                x.Id, x.Name, x.Description, x.City, x.CountryCode,
                x.CreatorLogin, x.PaymentValidUntil, x.CreatedAt))
            .ToList();
    }
}
