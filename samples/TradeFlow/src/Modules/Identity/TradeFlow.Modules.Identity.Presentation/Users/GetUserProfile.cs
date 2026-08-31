using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Identity.Application.Users.Dtos;
using TradeFlow.Modules.Identity.Application.Users.Queries;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Presentation.Users;

internal sealed class GetUserProfileEndpoint : EndpointWithoutRequest<UserProfileResponse>
{
    private readonly IMediator _mediator;

    public GetUserProfileEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/users/profile");
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
