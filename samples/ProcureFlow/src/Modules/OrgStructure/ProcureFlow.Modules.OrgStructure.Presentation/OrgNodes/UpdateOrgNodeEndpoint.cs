using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.OrgStructure.Application.Dtos;
using ProcureFlow.Modules.OrgStructure.Application.OrgNodes.Commands.UpdateOrgNode;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.OrgStructure.Presentation.OrgNodes;

internal sealed class UpdateOrgNodeEndpoint : Endpoint<UpdateOrgNodeEndpoint.Request, UpdateOrgNodeResponse>
{
    private readonly IMediator _mediator;
    public UpdateOrgNodeEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Put("/org-nodes/{orgNodeId}");
        Tag(Tags.OrgNodes);
        Summary("Update an organization node");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var command = new UpdateOrgNodeCommand(
            req.OrgNodeId, req.Name, req.NameBn, req.EffectiveTo, req.CustomsAttributesJson);
        Result<UpdateOrgNodeResponse> result = await _mediator.SendAsync(command, ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid OrgNodeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? NameBn { get; set; }
        public DateOnly? EffectiveTo { get; set; }
        public string? CustomsAttributesJson { get; set; }
    }
}
