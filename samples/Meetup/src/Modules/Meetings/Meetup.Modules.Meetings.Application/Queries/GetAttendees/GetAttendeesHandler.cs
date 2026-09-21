using Meetup.Modules.Meetings.Application.Dtos;
using Meetup.Modules.Meetings.Domain.Repositories;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Meetings.Application.Queries.GetAttendees;

public sealed class GetAttendeesHandler(IMeetingAttendeeRepository attendees)
    : IQueryHandler<GetAttendeesQuery, IReadOnlyList<MeetingAttendeeDto>>
{
    public async Task<IReadOnlyList<MeetingAttendeeDto>> HandleAsync(GetAttendeesQuery query, CancellationToken ct)
    {
        var items = await attendees.FindByMeetingAsync(query.MeetingId, ct);
        return items
            .Select(x => new MeetingAttendeeDto(
                x.Id, x.MeetingId, x.Login, x.GuestsCount, x.IsHost, x.Status.ToString()))
            .ToList();
    }
}
