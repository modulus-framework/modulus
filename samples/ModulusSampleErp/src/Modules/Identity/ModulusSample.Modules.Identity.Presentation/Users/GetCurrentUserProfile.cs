using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Identity.Application.Users.Dtos;
using ModulusSample.Modules.Identity.Application.Users.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Presentation.Users;

internal sealed class GetCurrentUserProfileEndpoint : EndpointWithoutRequest<UserProfileResponse>
{
    private readonly IMediator _mediator;

    public GetCurrentUserProfileEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/users");
        Tag(Tags.Users);
        Summary("Get current user profile");
    }

    protected override async Task HandleAsync(CancellationToken ct)
    {
        Result<UserProfileResponse> result = await _mediator.QueryAsync(new GetUserProfileQuery(), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
