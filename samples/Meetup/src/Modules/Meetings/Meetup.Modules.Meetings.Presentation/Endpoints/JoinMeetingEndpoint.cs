using Meetup.Modules.Meetings.Application.Commands.JoinMeeting;
using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Meetings.Presentation.Endpoints;

public sealed class JoinMeetingRequest
{
    public Guid MeetingId { get; set; }
    public string Login { get; set; } = string.Empty;
    public int GuestsCount { get; set; }
}

public sealed class JoinMeetingEndpoint(IMediator mediator) : Endpoint<JoinMeetingRequest, string>
{
    public override void Configure()
    {
        Post("/api/meetings/meetings/{meetingId}/join");
        Permissions(MeetingsPermissions.JoinMeeting);
        Summary("Joins a meeting (waitlist when the attendee limit is reached)");
    }

    public override async Task HandleAsync(JoinMeetingRequest req, CancellationToken ct)
    {
        var status = await mediator.SendAsync(
            new JoinMeetingCommand(req.MeetingId, req.Login, req.GuestsCount), ct);
        await SendOkAsync(status, ct);
    }
}
