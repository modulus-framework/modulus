using Meetup.Modules.Administration.Application.Dtos;
using Meetup.Modules.Administration.Application.Queries.GetProposals;
using Meetup.Shared.Presentation;
using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Administration.Presentation.Endpoints;

public sealed class GetProposalsEndpoint(IMediator mediator)
    : EndpointWithoutRequest<IReadOnlyList<MeetingGroupProposalDto>>
{
    public override void Configure()
    {
        Get("/api/administration/proposals");
        Roles(MeetupRoles.Admins, MeetupRoles.Organizers);
        Permissions(AdministrationPermissions.ViewProposals);
        Summary("Lists meeting group proposals");
    }

    protected override async Task HandleAsync(CancellationToken ct)
    {
        var items = await mediator.QueryAsync(new GetProposalsQuery(), ct);
        await SendOkAsync(items, ct);
    }
}
