using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Identity.Application.Users.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Presentation.Users;

internal sealed class ChangePasswordEndpoint : Endpoint<ChangePasswordEndpoint.ChangePasswordRequest>
{
    private readonly IMediator _mediator;

    public ChangePasswordEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Put("/users/{userId:guid}/password");
        Tag(Tags.Users);
        Summary("Change user password");
    }

    public override async Task HandleAsync(ChangePasswordRequest req, CancellationToken ct)
    {
        var command = new ChangePasswordCommand(req.UserId, req.CurrentPassword, req.NewPassword);
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
        public Guid UserId { get; set; }
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
}
