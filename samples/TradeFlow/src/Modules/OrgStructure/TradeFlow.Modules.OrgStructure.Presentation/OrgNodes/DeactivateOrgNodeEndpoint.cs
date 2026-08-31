using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.OrgStructure.Application.OrgNodes.Commands.DeactivateOrgNode;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.OrgStructure.Presentation.OrgNodes;

internal sealed class DeactivateOrgNodeEndpoint : Endpoint<DeactivateOrgNodeEndpoint.Request, object>
{
    private readonly IMediator _mediator;
    public DeactivateOrgNodeEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/org-nodes/{orgNodeId}/deactivate");
        Tag(Tags.OrgNodes);
        Summary("Deactivate an organization node");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new DeactivateOrgNodeCommand(req.OrgNodeId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid OrgNodeId { get; set; }
    }
}
