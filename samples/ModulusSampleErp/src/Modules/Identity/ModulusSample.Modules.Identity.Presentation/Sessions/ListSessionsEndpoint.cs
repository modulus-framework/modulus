using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Identity.Application.Sessions.Dtos;
using ModulusSample.Modules.Identity.Application.Sessions.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Presentation.Sessions;

internal sealed class ListSessionsEndpoint : EndpointWithoutRequest<List<SessionDto>>
{
    private readonly IMediator _mediator;

    public ListSessionsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/sessions");
        Tag(Tags.Sessions);
        Summary("List active sessions");
    }

    protected override async Task HandleAsync(CancellationToken ct)
    {
        Result<List<SessionDto>> result = await _mediator.QueryAsync(new ListSessionsQuery(), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
