using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Identity.Application.Users.Dtos;
using ProcureFlow.Modules.Identity.Application.Users.Queries;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Presentation.Admin;

internal sealed class GetUserByUsernameEndpoint : Endpoint<GetUserByUsernameEndpoint.GetUserByUsernameRequest, UserProfileResponse>
{
    private readonly IMediator _mediator;

    public GetUserByUsernameEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/admin/users/by-username/{username}");
        Tag(Tags.AdminUsers);
        Summary("Get user by username");
    }

    public override async Task HandleAsync(GetUserByUsernameRequest req, CancellationToken ct)
    {
        Result<UserProfileResponse> result = await _mediator.QueryAsync(new GetUserByUsernameQuery(req.Username), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetUserByUsernameRequest
    {
        public string Username { get; set; } = string.Empty;
    }
}
