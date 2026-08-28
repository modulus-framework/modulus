using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Identity.Application.Users.Commands;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Presentation.Users;

internal sealed class UpdateProfileEndpoint : Endpoint<UpdateProfileEndpoint.UpdateProfileRequest>
{
    private readonly IMediator _mediator;

    public UpdateProfileEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Put("/users/{userId:guid}/profile");
        Tag(Tags.Users);
        Summary("Update user profile");
    }

    public override async Task HandleAsync(UpdateProfileRequest req, CancellationToken ct)
    {
        var command = new UpdateProfileCommand(
            req.UserId, req.FirstName, req.LastName, req.PhoneNumber, req.ProfileImageUrl);

        Result result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class UpdateProfileRequest
    {
        public Guid UserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? ProfileImageUrl { get; set; }
    }
}
