using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.OrgStructure.Application.Dtos;
using ProcureFlow.Modules.OrgStructure.Application.Positions.Queries.GetPositionById;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.OrgStructure.Presentation.Positions;

internal sealed class GetPositionByIdEndpoint : Endpoint<GetPositionByIdEndpoint.Request, PositionResponse>
{
    private readonly IMediator _mediator;
    public GetPositionByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/positions/{positionId}");
        Tag(Tags.Positions);
        Summary("Get a position by ID with assignments");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<PositionResponse> result = await _mediator.QueryAsync(new GetPositionByIdQuery(req.PositionId), ct);
        if (result.IsFailure) { await EndpointHelper.SendFailureAsync(HttpContext, result, ct); return; }
        await SendOkAsync(result.Value, ct);
    }

    internal sealed class Request
    {
        public Guid PositionId { get; set; }
    }
}
