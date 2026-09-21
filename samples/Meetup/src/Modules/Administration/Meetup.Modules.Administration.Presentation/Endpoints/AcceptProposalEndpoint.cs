using Meetup.Modules.Administration.Application.Commands.AcceptProposal;
using Meetup.Shared.Presentation;
using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Administration.Presentation.Endpoints;

public sealed class AcceptProposalEndpoint(IMediator mediator) : Endpoint<AcceptProposalEndpoint.AcceptProposalRequest>
{
    public override void Configure()
    {
        Post("/api/administration/proposals/{id}/accept");
        Roles(MeetupRoles.Admins);
        Permissions(AdministrationPermissions.DecideProposal);
        Summary("Accepts a proposal (creates the meeting group via integration event)");
    }

    public override async Task HandleAsync(AcceptProposalRequest req, CancellationToken ct)
    {
        await mediator.SendAsync(new AcceptProposalCommand(req.Id), ct);
        await SendNoContentAsync(ct);
    }

    public sealed class AcceptProposalRequest
    {
        public Guid Id { get; set; }
    }
}
