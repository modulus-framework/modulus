using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Identity.Application.Sessions.Commands;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Presentation.Sessions;

internal sealed class RevokeSessionEndpoint : Endpoint<RevokeSessionEndpoint.RevokeSessionRequest>
{
    private readonly IMediator _mediator;

    public RevokeSessionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/sessions/{id:guid}/revoke");
        Tag(Tags.Sessions);
        Summary("Revoke a specific session");
    }

    public override async Task HandleAsync(RevokeSessionRequest req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new RevokeSessionCommand(req.Id), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class RevokeSessionRequest
    {
        public Guid Id { get; set; }
    }
}
