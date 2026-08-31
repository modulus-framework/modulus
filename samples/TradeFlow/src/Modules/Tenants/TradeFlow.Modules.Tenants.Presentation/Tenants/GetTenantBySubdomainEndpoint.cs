using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Tenants.Application.Tenants.Queries;
using TradeFlow.Modules.Tenants.Application.Tenants.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Tenants.Presentation.Tenants;

internal sealed class GetTenantBySubdomainEndpoint : Endpoint<GetTenantBySubdomainEndpoint.GetBySubdomainRequest, TenantDto>
{
    private readonly IMediator _mediator;

    public GetTenantBySubdomainEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/tenants/subdomain/{subdomain}");
        Tag(Tags.Tenants);
        Summary("Get a tenant by subdomain");
    }

    public override async Task HandleAsync(GetBySubdomainRequest req, CancellationToken ct)
    {
        var query = new GetTenantBySubdomainQuery(req.Subdomain);
        Result<TenantDto> result = await _mediator.QueryAsync(query, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetBySubdomainRequest
    {
        public string Subdomain { get; set; } = string.Empty;
    }
}
