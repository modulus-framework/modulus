using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Tenants.Application.Tenants.Queries;
using TradeFlow.Modules.Tenants.Application.Tenants.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Tenants.Presentation.Tenants;

internal sealed class GetTenantByIdEndpoint : Endpoint<GetTenantByIdEndpoint.GetByIdRequest, TenantDto>
{
    private readonly IMediator _mediator;

    public GetTenantByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/tenants/{tenantId}");
        Tag(Tags.Tenants);
        Summary("Get a tenant by ID");
    }

    public override async Task HandleAsync(GetByIdRequest req, CancellationToken ct)
    {
        var query = new GetTenantByIdQuery(req.TenantId);
        Result<TenantDto> result = await _mediator.QueryAsync(query, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetByIdRequest
    {
        public Guid TenantId { get; set; }
    }
}
