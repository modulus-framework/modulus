using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Identity.Application.Sessions.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Presentation.Sessions;

internal sealed class RevokeOtherSessionsEndpoint : Endpoint<EmptyRequest, RevokeOtherSessionsResponse>
{
    private readonly IMediator _mediator;

    public RevokeOtherSessionsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/sessions/revoke-others");
        Tag(Tags.Sessions);
        Summary("Revoke all other sessions");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        Result<RevokeOtherSessionsResponse> result = await _mediator.SendAsync(new RevokeOtherSessionsCommand(), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
