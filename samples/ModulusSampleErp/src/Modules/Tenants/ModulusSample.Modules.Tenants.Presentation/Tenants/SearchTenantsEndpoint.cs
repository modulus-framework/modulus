using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Tenants.Application.Tenants.Queries;
using ModulusSample.Modules.Tenants.Application.Tenants.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Tenants.Presentation.Tenants;

internal sealed class SearchTenantsEndpoint : Endpoint<SearchTenantsEndpoint.SearchTenantsRequest, PagedResult<TenantDto>>
{
    private readonly IMediator _mediator;

    public SearchTenantsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/tenants/search");
        Tag(Tags.Tenants);
        Summary("Search tenants by name or subdomain");
    }

    public override async Task HandleAsync(SearchTenantsRequest req, CancellationToken ct)
    {
        var query = new SearchTenantsQuery(req.SearchTerm, req.Page, req.PageSize);
        Result<PagedResult<TenantDto>> result = await _mediator.QueryAsync(query, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class SearchTenantsRequest
    {
        public string SearchTerm { get; set; } = string.Empty;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}