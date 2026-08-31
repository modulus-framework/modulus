using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.OrgStructure.Application.Dtos;
using TradeFlow.Modules.OrgStructure.Application.OrgNodes.Queries.GetOrgNodesByParent;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.OrgStructure.Presentation.OrgNodes;

internal sealed class GetOrgNodesByParentEndpoint : Endpoint<GetOrgNodesByParentEndpoint.Request, IReadOnlyList<OrgNodeResponse>>
{
    private readonly IMediator _mediator;
    public GetOrgNodesByParentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/org-nodes/children");
        Tag(Tags.OrgNodes);
        Summary("Get child nodes of a parent (or root nodes when parentId is null)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<OrgNodeResponse>> result = await _mediator.QueryAsync(
            new GetOrgNodesByParentQuery(req.ParentId), ct);
        if (result.IsFailure) { await EndpointHelper.SendFailureAsync(HttpContext, result, ct); return; }
        await SendOkAsync(result.Value, ct);
    }

    internal sealed class Request
    {
        public Guid? ParentId { get; set; }
    }
}
