using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Identity.Application.Roles.Dtos;
using ProcureFlow.Modules.Identity.Application.Roles.Queries;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Presentation.Users;

internal sealed class GetMyRolesEndpoint : EndpointWithoutRequest<MyRolesResponse>
{
    private readonly IMediator _mediator;

    public GetMyRolesEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/users/roles");
        Tag(Tags.Users);
        Summary("Get current user roles");
    }

    protected override async Task HandleAsync(CancellationToken ct)
    {
        Result<MyRolesResponse> result = await _mediator.QueryAsync(new GetMyRolesQuery(), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
