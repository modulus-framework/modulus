using Meetup.Modules.Meetings.Application.Dtos;
using Meetup.Modules.Meetings.Application.Queries.GetMeetings;
using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Meetings.Presentation.Endpoints;

public sealed class GetMeetingsRequest
{
    public Guid GroupId { get; set; }
}

public sealed class GetMeetingsEndpoint(IMediator mediator)
    : Endpoint<GetMeetingsRequest, IReadOnlyList<MeetingDto>>
{
    public override void Configure()
    {
        Get("/api/meetings/groups/{groupId}/meetings");
        Permissions(MeetingsPermissions.ViewMeetings);
        Summary("Lists meetings of a group");
    }

    public override async Task HandleAsync(GetMeetingsRequest req, CancellationToken ct)
    {
        var items = await mediator.QueryAsync(new GetMeetingsQuery(req.GroupId), ct);
        await SendOkAsync(items, ct);
    }
}
