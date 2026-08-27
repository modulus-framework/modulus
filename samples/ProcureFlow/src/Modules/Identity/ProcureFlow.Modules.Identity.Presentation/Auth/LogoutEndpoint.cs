using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Identity.Application.Users.Commands;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Presentation.Auth;

internal sealed class LogoutEndpoint : Endpoint<LogoutEndpoint.LogoutRequest, LogoutResponse>
{
    private readonly IMediator _mediator;

    public LogoutEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/auth/logout");
        Summary("Logout user with end-session support");
        Tag(Tags.Auth);
    }

    public override async Task HandleAsync(LogoutRequest req, CancellationToken ct)
    {
        Result<LogoutResponse> result = await _mediator.SendAsync(new LogoutCommand(req.IdTokenHint), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class LogoutRequest
    {
        public string? IdTokenHint { get; set; }
    }
}
