using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Identity.Application.Users.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Presentation.Admin;

internal sealed class ActivateUserEndpoint : Endpoint<ActivateUserEndpoint.ActivateUserRequest>
{
    private readonly IMediator _mediator;

    public ActivateUserEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/admin/users/{userId:guid}/activate");
        Tag(Tags.AdminUsers);
        Summary("Activate user account");
    }

    public override async Task HandleAsync(ActivateUserRequest req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new ActivateUserCommand(req.UserId), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class ActivateUserRequest
    {
        public Guid UserId { get; set; }
    }
}
