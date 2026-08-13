using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Tenants.Application.Tenants.Queries;
using ModulusSample.Modules.Tenants.Application.Tenants.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Tenants.Presentation.Tenants;

internal sealed class GetAllTenantsEndpoint : Endpoint<GetAllTenantsEndpoint.GetAllTenantsRequest, PagedResult<TenantDto>>
{
    private readonly IMediator _mediator;

    public GetAllTenantsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/tenants");
        Tag(Tags.Tenants);
        Summary("Get all tenants with optional filtering");
    }

    public override async Task HandleAsync(GetAllTenantsRequest req, CancellationToken ct)
    {
        var query = new GetAllTenantsQuery(req.IsActive, req.Page, req.PageSize);
        Result<PagedResult<TenantDto>> result = await _mediator.QueryAsync(query, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetAllTenantsRequest
    {
        public bool? IsActive { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}
