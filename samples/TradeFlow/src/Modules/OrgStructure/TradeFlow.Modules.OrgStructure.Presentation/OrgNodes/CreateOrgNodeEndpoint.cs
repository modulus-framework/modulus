using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.OrgStructure.Application.Dtos;
using TradeFlow.Modules.OrgStructure.Application.OrgNodes.Commands.CreateOrgNode;
using TradeFlow.Modules.OrgStructure.Domain.Enums;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.OrgStructure.Presentation.OrgNodes;

internal sealed class CreateOrgNodeEndpoint : Endpoint<CreateOrgNodeEndpoint.Request, CreateOrgNodeResponse>
{
    private readonly IMediator _mediator;
    public CreateOrgNodeEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/org-nodes");
        Tag(Tags.OrgNodes);
        Summary("Create an organization node (company, BU, site, department, or position node)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var command = new CreateOrgNodeCommand(
            req.ParentId, req.NodeType, req.Code, req.Name, req.NameBn,
            req.EffectiveFrom, req.EffectiveTo, req.CustomsAttributesJson);
        Result<CreateOrgNodeResponse> result = await _mediator.SendAsync(command, ct);
        if (result.IsFailure) { await EndpointHelper.SendFailureAsync(HttpContext, result, ct); return; }
        await SendCreatedAsync(result.Value, $"/api/v1/org-nodes/{result.Value.OrgNodeId}", ct);
    }

    internal sealed class Request
    {
        public Guid? ParentId { get; set; }
        public OrgNodeType NodeType { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? NameBn { get; set; }
        public DateOnly EffectiveFrom { get; set; }
        public DateOnly? EffectiveTo { get; set; }
        public string? CustomsAttributesJson { get; set; }
    }
}
