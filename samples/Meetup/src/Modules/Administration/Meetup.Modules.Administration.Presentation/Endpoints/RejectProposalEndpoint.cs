using Meetup.Modules.Administration.Application.Commands.RejectProposal;
using Meetup.Shared.Presentation;
using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Administration.Presentation.Endpoints;

public sealed class RejectProposalEndpoint(IMediator mediator) : Endpoint<RejectProposalEndpoint.RejectProposalRequest>
{
    public override void Configure()
    {
        Post("/api/administration/proposals/{id}/reject");
        Roles(MeetupRoles.Admins);
        Permissions(AdministrationPermissions.DecideProposal);
        Summary("Rejects a proposal");
    }

    public override async Task HandleAsync(RejectProposalRequest req, CancellationToken ct)
    {
        await mediator.SendAsync(new RejectProposalCommand(req.Id), ct);
        await SendNoContentAsync(ct);
    }

    public sealed class RejectProposalRequest
    {
        public Guid Id { get; set; }
    }
}
