using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.OrgStructure.Application.Dtos;
using TradeFlow.Modules.OrgStructure.Application.Positions.Commands.AssignPosition;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.OrgStructure.Presentation.Positions;

internal sealed class AssignPositionEndpoint : Endpoint<AssignPositionEndpoint.Request, AssignPositionResponse>
{
    private readonly IMediator _mediator;
    public AssignPositionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/positions/{positionId}/assignments");
        Tag(Tags.Positions);
        Summary("Assign a user to a position");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var command = new AssignPositionCommand(
            req.PositionId, req.UserId, req.EffectiveFrom, req.EffectiveTo);
        Result<AssignPositionResponse> result = await _mediator.SendAsync(command, ct);
        if (result.IsFailure) { await EndpointHelper.SendFailureAsync(HttpContext, result, ct); return; }
        await SendCreatedAsync(result.Value, $"/api/v1/positions/{req.PositionId}", ct);
    }

    internal sealed class Request
    {
        public Guid PositionId { get; set; }
        public Guid UserId { get; set; }
        public DateOnly EffectiveFrom { get; set; }
        public DateOnly? EffectiveTo { get; set; }
    }
}
