using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Tenants.Application.Tenants.Commands;
using TradeFlow.Modules.Tenants.Application.Tenants.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Tenants.Presentation.Tenants;

internal sealed class UpdateTenantEndpoint : Endpoint<UpdateTenantEndpoint.UpdateTenantRequest, UpdateTenantResponse>
{
    private readonly IMediator _mediator;

    public UpdateTenantEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Put("/tenants/{tenantId}");
        Tag(Tags.Tenants);
        Summary("Update tenant details");
    }

    public override async Task HandleAsync(UpdateTenantRequest req, CancellationToken ct)
    {
        var command = new UpdateTenantCommand(req.TenantId, req.Name, req.DatabaseConnectionString);
        Result<UpdateTenantResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class UpdateTenantRequest
    {
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DatabaseConnectionString { get; set; } = string.Empty;
    }
}
