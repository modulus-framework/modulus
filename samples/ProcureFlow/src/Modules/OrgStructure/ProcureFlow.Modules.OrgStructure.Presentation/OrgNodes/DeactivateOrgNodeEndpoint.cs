using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.OrgStructure.Application.OrgNodes.Commands.DeactivateOrgNode;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.OrgStructure.Presentation.OrgNodes;

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
