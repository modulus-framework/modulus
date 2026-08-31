using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Identity.Application.Users.Commands;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Presentation.Users;

internal sealed class ChangePasswordEndpoint : Endpoint<ChangePasswordEndpoint.ChangePasswordRequest>
{
    private readonly IMediator _mediator;

    public ChangePasswordEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Put("/users/password");
        Tag(Tags.Users);
        Summary("Change current user password");
    }

    public override async Task HandleAsync(ChangePasswordRequest req, CancellationToken ct)
    {
        var command = new ChangePasswordCommand(req.CurrentPassword, req.NewPassword);
        Result result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class ChangePasswordRequest
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
}
