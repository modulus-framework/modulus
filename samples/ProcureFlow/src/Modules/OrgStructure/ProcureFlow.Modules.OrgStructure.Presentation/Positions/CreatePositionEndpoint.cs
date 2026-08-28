using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.OrgStructure.Application.Dtos;
using ProcureFlow.Modules.OrgStructure.Application.Positions.Commands.CreatePosition;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.OrgStructure.Presentation.Positions;

internal sealed class CreatePositionEndpoint : Endpoint<CreatePositionEndpoint.Request, CreatePositionResponse>
{
    private readonly IMediator _mediator;
    public CreatePositionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/positions");
        Tag(Tags.Positions);
        Summary("Create a position within an organization node");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var command = new CreatePositionCommand(
            req.OrgNodeId, req.Code, req.Title, req.TitleBn, req.IsDelegatable);
        Result<CreatePositionResponse> result = await _mediator.SendAsync(command, ct);
        if (result.IsFailure) { await EndpointHelper.SendFailureAsync(HttpContext, result, ct); return; }
        await SendCreatedAsync(result.Value, $"/api/v1/positions/{result.Value.PositionId}", ct);
    }

    internal sealed class Request
    {
        public Guid OrgNodeId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? TitleBn { get; set; }
        public bool IsDelegatable { get; set; } = true;
    }
}
