using Meetup.Modules.Meetings.Application.Dtos;
using Meetup.Modules.Meetings.Application.Queries.GetAttendees;
using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Meetings.Presentation.Endpoints;

public sealed class GetAttendeesRequest
{
    public Guid MeetingId { get; set; }
}

public sealed class GetAttendeesEndpoint(IMediator mediator)
    : Endpoint<GetAttendeesRequest, IReadOnlyList<MeetingAttendeeDto>>
{
    public override void Configure()
    {
        Get("/api/meetings/meetings/{meetingId}/attendees");
        Permissions(MeetingsPermissions.ViewMeetings);
        Summary("Lists meeting attendees");
    }

    public override async Task HandleAsync(GetAttendeesRequest req, CancellationToken ct)
    {
        var items = await mediator.QueryAsync(new GetAttendeesQuery(req.MeetingId), ct);
        await SendOkAsync(items, ct);
    }
}
