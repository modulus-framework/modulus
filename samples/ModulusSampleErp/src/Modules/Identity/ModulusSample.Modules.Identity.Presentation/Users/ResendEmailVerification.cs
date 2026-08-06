using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Identity.Application.Users.Commands;
using ModulusSample.Shared.Domain;
using Microsoft.AspNetCore.Http;

namespace ModulusSample.Modules.Identity.Presentation.Users;

internal sealed class ResendEmailVerificationEndpoint : Endpoint<EmptyRequest>
{
    private readonly IMediator _mediator;

    public ResendEmailVerificationEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/users/email/verify/resend");
        Tag(Tags.Users);
        Summary("Resend email verification token");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new ResendEmailVerificationCommand(), ct);

        if (result.IsFailure)
        {
            if (result.Error.Code == "RateLimit.Exceeded")
            {
                await SendErrorAsync(StatusCodes.Status429TooManyRequests, result.Error.Message, ct);
                return;
            }

            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }
}
