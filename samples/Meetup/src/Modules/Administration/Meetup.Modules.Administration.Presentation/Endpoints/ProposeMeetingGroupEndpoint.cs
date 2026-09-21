using Meetup.Modules.Administration.Application.Commands.ProposeMeetingGroup;
using Meetup.Shared.Presentation;
using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Administration.Presentation.Endpoints;

public sealed class ProposeMeetingGroupEndpoint(IMediator mediator) : Endpoint<ProposeMeetingGroupEndpoint.ProposeMeetingGroupRequest, Guid>
{
    public override void Configure()
    {
        Post("/api/administration/proposals");
        Roles(MeetupRoles.Organizers);
        Permissions(AdministrationPermissions.ProposeMeetingGroup);
        Summary("Proposes a new meeting group");
    }

    public override async Task HandleAsync(ProposeMeetingGroupRequest req, CancellationToken ct)
    {
        var id = await mediator.SendAsync(new ProposeMeetingGroupCommand(
            req.Name, req.Description, req.City, req.CountryCode, req.ProposerLogin), ct);
        await SendCreatedAsync(id, $"/api/administration/proposals/{id}", ct);
    }

    public sealed class ProposeMeetingGroupRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string CountryCode { get; set; } = string.Empty;
        public string ProposerLogin { get; set; } = string.Empty;
    }
}
