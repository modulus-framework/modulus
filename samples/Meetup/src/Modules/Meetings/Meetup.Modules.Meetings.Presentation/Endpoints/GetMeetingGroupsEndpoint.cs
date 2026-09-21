using Meetup.Modules.Meetings.Application.Dtos;
using Meetup.Modules.Meetings.Application.Queries.GetMeetingGroups;
using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Meetings.Presentation.Endpoints;

public sealed class GetMeetingGroupsEndpoint(IMediator mediator)
    : EndpointWithoutRequest<IReadOnlyList<MeetingGroupDto>>
{
    public override void Configure()
    {
        Get("/api/meetings/groups");
        Permissions(MeetingsPermissions.ViewMeetings);
        Summary("Lists meeting groups");
    }

    protected override async Task HandleAsync(CancellationToken ct)
    {
        var items = await mediator.QueryAsync(new GetMeetingGroupsQuery(), ct);
        await SendOkAsync(items, ct);
    }
}
