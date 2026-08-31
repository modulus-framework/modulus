using Modulus.AspNetCore.Endpoints;
using System.Text.Json;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Tenants.Application.Tenants.Commands;
using TradeFlow.Modules.Tenants.Application.Tenants.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Tenants.Presentation.Tenants;

internal sealed class UpdateTenantFeaturesEndpoint : Endpoint<UpdateTenantFeaturesEndpoint.UpdateFeaturesRequest, UpdateTenantResponse>
{
    private readonly IMediator _mediator;

    public UpdateTenantFeaturesEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Put("/tenants/{tenantId}/features");
        Tag(Tags.Tenants);
        Summary("Update tenant feature flags");
    }

    public override async Task HandleAsync(UpdateFeaturesRequest req, CancellationToken ct)
    {
        var command = new UpdateTenantFeaturesCommand(req.TenantId, req.Features);
        Result<UpdateTenantResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class UpdateFeaturesRequest
    {
        public Guid TenantId { get; set; }
        public JsonDocument Features { get; set; } = JsonDocument.Parse("{}");
    }
}
