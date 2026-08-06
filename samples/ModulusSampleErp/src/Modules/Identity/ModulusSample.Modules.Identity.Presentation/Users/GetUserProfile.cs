using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Identity.Application.Users.Dtos;
using ModulusSample.Modules.Identity.Application.Users.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Presentation.Users;

internal sealed class GetUserProfileEndpoint : Endpoint<GetUserProfileEndpoint.GetUserProfileRequest, UserProfileResponse>
{
    private readonly IMediator _mediator;

    public GetUserProfileEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/users/{userId:guid}");
        Tag(Tags.Users);
        Summary("Get user profile");
    }

    public override async Task HandleAsync(GetUserProfileRequest req, CancellationToken ct)
    {
        Result<UserProfileResponse> result = await _mediator.QueryAsync(new GetUserProfileQuery(req.UserId), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetUserProfileRequest
    {
        public Guid UserId { get; set; }
    }
}
