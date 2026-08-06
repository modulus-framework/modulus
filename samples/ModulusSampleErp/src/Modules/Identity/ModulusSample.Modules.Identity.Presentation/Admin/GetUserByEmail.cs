using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Identity.Application.Users.Dtos;
using ModulusSample.Modules.Identity.Application.Users.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Presentation.Admin;

internal sealed class GetUserByEmailEndpoint : Endpoint<GetUserByEmailEndpoint.GetUserByEmailRequest, UserProfileResponse>
{
    private readonly IMediator _mediator;

    public GetUserByEmailEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/admin/users/by-email/{email}");
        Tag(Tags.AdminUsers);
        Summary("Get user by email");
    }

    public override async Task HandleAsync(GetUserByEmailRequest req, CancellationToken ct)
    {
        Result<UserProfileResponse> result = await _mediator.QueryAsync(new GetUserByEmailQuery(req.Email), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetUserByEmailRequest
    {
        public string Email { get; set; } = string.Empty;
    }
}
