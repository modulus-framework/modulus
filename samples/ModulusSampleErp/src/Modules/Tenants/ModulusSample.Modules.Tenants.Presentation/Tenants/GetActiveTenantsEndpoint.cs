using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Tenants.Application.Tenants.Queries;
using ModulusSample.Modules.Tenants.Application.Tenants.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Tenants.Presentation.Tenants;

internal sealed class GetActiveTenantsEndpoint : EndpointWithoutRequest<IReadOnlyList<TenantDto>>
{
    private readonly IMediator _mediator;

    public GetActiveTenantsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/tenants/active");
        Tag(Tags.Tenants);
        Summary("Get all active tenants");
        RequireAuthorization();
    }

    protected override async Task HandleAsync(CancellationToken ct)
    {
        Result<IReadOnlyList<TenantDto>> result = await _mediator.QueryAsync(new GetActiveTenantsQuery(), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
