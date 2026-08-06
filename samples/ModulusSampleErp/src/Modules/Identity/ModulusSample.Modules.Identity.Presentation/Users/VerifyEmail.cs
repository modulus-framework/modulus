using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Identity.Application.Users.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Presentation.Users;

internal sealed class VerifyEmailEndpoint : Endpoint<VerifyEmailEndpoint.VerifyEmailRequest>
{
    private readonly IMediator _mediator;

    public VerifyEmailEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/users/email/verify");
        AllowAnonymous();
        Tag(Tags.Users);
        Summary("Verify user email address");
    }

    public override async Task HandleAsync(VerifyEmailRequest req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new VerifyEmailCommand(req.Token), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class VerifyEmailRequest
    {
        public string Token { get; set; } = string.Empty;
    }
}
