using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Identity.Application.Roles.Dtos;
using ModulusSample.Modules.Identity.Application.Roles.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Presentation.Roles;

internal sealed class GetRolesEndpoint : EndpointWithoutRequest<List<RoleResponse>>
{
    private readonly IMediator _mediator;

    public GetRolesEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/roles");
        Tag(Tags.Roles);
        Summary("Get all roles");
    }

    protected override async Task HandleAsync(CancellationToken ct)
    {
        Result<List<RoleResponse>> result = await _mediator.QueryAsync(new GetRolesQuery(), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
