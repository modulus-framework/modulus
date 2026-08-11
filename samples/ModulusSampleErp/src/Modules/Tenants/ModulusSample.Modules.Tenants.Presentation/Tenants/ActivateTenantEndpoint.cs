using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Tenants.Application.Tenants.Commands;
using ModulusSample.Modules.Tenants.Application.Tenants.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Tenants.Presentation.Tenants;

internal sealed class ActivateTenantEndpoint : Endpoint<ActivateTenantEndpoint.TenantRouteRequest, TenantStatusResponse>
{
    private readonly IMediator _mediator;

    public ActivateTenantEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/tenants/{tenantId}/activate");
        Tag(Tags.Tenants);
        Summary("Activate a tenant");
    }

    public override async Task HandleAsync(TenantRouteRequest req, CancellationToken ct)
    {
        var command = new ActivateTenantCommand(req.TenantId);
        Result<TenantStatusResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class TenantRouteRequest
    {
        public Guid TenantId { get; set; }
    }
}
