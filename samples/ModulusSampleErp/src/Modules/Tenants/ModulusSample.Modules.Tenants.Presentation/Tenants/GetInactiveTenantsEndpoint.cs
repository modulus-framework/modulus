using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Tenants.Application.Tenants.Queries;
using ModulusSample.Modules.Tenants.Application.Tenants.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Tenants.Presentation.Tenants;

internal sealed class GetInactiveTenantsEndpoint : EndpointWithoutRequest<IReadOnlyList<TenantDto>>
{
    private readonly IMediator _mediator;

    public GetInactiveTenantsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/tenants/inactive");
        Tag(Tags.Tenants);
        Summary("Get all inactive tenants");
    }

    protected override async Task HandleAsync(CancellationToken ct)
    {
        Result<IReadOnlyList<TenantDto>> result = await _mediator.QueryAsync(new GetInactiveTenantsQuery(), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}