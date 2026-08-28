using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.OrgStructure.Application.Dtos;
using ProcureFlow.Modules.OrgStructure.Application.OrgNodes.Queries.GetOrgTree;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.OrgStructure.Presentation.OrgNodes;

internal sealed class GetOrgTreeEndpoint : Endpoint<GetOrgTreeEndpoint.EmptyRequest, IReadOnlyList<OrgNodeResponse>>
{
    private readonly IMediator _mediator;
    public GetOrgTreeEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/org-tree");
        Tag(Tags.OrgNodes);
        Summary("Get the full organization tree (all nodes, depth-sorted)");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        Result<IReadOnlyList<OrgNodeResponse>> result = await _mediator.QueryAsync(new GetOrgTreeQuery(), ct);
        if (result.IsFailure) { await EndpointHelper.SendFailureAsync(HttpContext, result, ct); return; }
        await SendOkAsync(result.Value, ct);
    }

    internal sealed class EmptyRequest { }
}
