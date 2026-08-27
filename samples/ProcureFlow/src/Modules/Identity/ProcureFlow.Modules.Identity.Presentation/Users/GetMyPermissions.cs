using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Identity.Application.Permissions.Dtos;
using ProcureFlow.Modules.Identity.Application.Permissions.Queries;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Presentation.Users;

internal sealed class GetMyPermissionsEndpoint : EndpointWithoutRequest<MyPermissionsResponse>
{
    private readonly IMediator _mediator;

    public GetMyPermissionsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/users/permissions");
        Tag(Tags.Users);
        Summary("Get current user permissions");
    }

    protected override async Task HandleAsync(CancellationToken ct)
    {
        Result<MyPermissionsResponse> result = await _mediator.QueryAsync(new GetMyPermissionsQuery(), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
