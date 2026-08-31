using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.OrgStructure.Application.Dtos;
using TradeFlow.Modules.OrgStructure.Application.OrgNodes.Queries.GetOrgNodeById;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.OrgStructure.Presentation.OrgNodes;

internal sealed class GetOrgNodeByIdEndpoint : Endpoint<GetOrgNodeByIdEndpoint.Request, OrgNodeDetailResponse>
{
    private readonly IMediator _mediator;
    public GetOrgNodeByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/org-nodes/{orgNodeId}");
        Tag(Tags.OrgNodes);
        Summary("Get an organization node with children and positions");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<OrgNodeDetailResponse> result = await _mediator.QueryAsync(new GetOrgNodeByIdQuery(req.OrgNodeId), ct);
        if (result.IsFailure) { await EndpointHelper.SendFailureAsync(HttpContext, result, ct); return; }
        await SendOkAsync(result.Value, ct);
    }

    internal sealed class Request
    {
        public Guid OrgNodeId { get; set; }
    }
}
