using Meetup.Modules.Meetings.Application.Commands.CreateMeeting;
using Meetup.Shared.Presentation;
using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Meetings.Presentation.Endpoints;

public sealed class CreateMeetingRequest
{
    public Guid GroupId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public int? AttendeesLimit { get; set; }
    public int GuestsLimit { get; set; }
    public decimal EventFee { get; set; }
    public string EventFeeCurrency { get; set; } = "USD";
    public string CreatorLogin { get; set; } = string.Empty;
}

public sealed class CreateMeetingEndpoint(IMediator mediator) : Endpoint<CreateMeetingRequest, Guid>
{
    public override void Configure()
    {
        Post("/api/meetings/meetings");
        Roles(MeetupRoles.Organizers);
        Permissions(MeetingsPermissions.CreateMeeting);
        Summary("Creates a meeting (organizer of a paid group only)");
    }

    public override async Task HandleAsync(CreateMeetingRequest req, CancellationToken ct)
    {
        var id = await mediator.SendAsync(new CreateMeetingCommand(
            req.GroupId, req.Title, req.Description, req.StartUtc, req.EndUtc,
            req.AttendeesLimit, req.GuestsLimit, req.EventFee, req.EventFeeCurrency,
            req.CreatorLogin), ct);
        await SendCreatedAsync(id, $"/api/meetings/meetings/{id}", ct);
    }
}
