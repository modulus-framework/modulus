using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.OrgStructure.Application.Dtos;
using TradeFlow.Modules.OrgStructure.Application.Positions.Queries.GetPositionsByOrgNode;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.OrgStructure.Presentation.Positions;

internal sealed class GetPositionsByOrgNodeEndpoint : Endpoint<GetPositionsByOrgNodeEndpoint.Request, IReadOnlyList<PositionResponse>>
{
    private readonly IMediator _mediator;
    public GetPositionsByOrgNodeEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/org-nodes/{orgNodeId}/positions");
        Tag(Tags.Positions);
        Summary("Get all positions within an organization node");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<PositionResponse>> result = await _mediator.QueryAsync(
            new GetPositionsByOrgNodeQuery(req.OrgNodeId), ct);
        if (result.IsFailure) { await EndpointHelper.SendFailureAsync(HttpContext, result, ct); return; }
        await SendOkAsync(result.Value, ct);
    }

    internal sealed class Request
    {
        public Guid OrgNodeId { get; set; }
    }
}
