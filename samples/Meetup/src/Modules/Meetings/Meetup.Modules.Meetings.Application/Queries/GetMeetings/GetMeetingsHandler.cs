using Meetup.Modules.Meetings.Application.Dtos;
using Meetup.Modules.Meetings.Domain.Repositories;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Meetings.Application.Queries.GetMeetings;

public sealed class GetMeetingsHandler(IMeetingRepository meetings)
    : IQueryHandler<GetMeetingsQuery, IReadOnlyList<MeetingDto>>
{
    public async Task<IReadOnlyList<MeetingDto>> HandleAsync(GetMeetingsQuery query, CancellationToken ct)
    {
        var items = await meetings.FindByGroupAsync(query.GroupId, ct);
        return items
            .Select(x => new MeetingDto(
                x.Id, x.GroupId, x.Title, x.Description, x.StartUtc, x.EndUtc,
                x.AttendeesLimit, x.GuestsLimit, x.EventFee, x.EventFeeCurrency, x.CreatorLogin))
            .ToList();
    }
}
