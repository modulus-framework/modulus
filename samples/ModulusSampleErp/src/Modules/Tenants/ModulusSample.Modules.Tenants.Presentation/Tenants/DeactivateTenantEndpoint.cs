using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Tenants.Application.Tenants.Commands;
using ModulusSample.Modules.Tenants.Application.Tenants.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Tenants.Presentation.Tenants;

internal sealed class DeactivateTenantEndpoint : Endpoint<DeactivateTenantEndpoint.TenantRouteRequest, TenantStatusResponse>
{
    private readonly IMediator _mediator;

    public DeactivateTenantEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/tenants/{tenantId}/deactivate");
        Tag(Tags.Tenants);
        Summary("Deactivate a tenant");
    }

    public override async Task HandleAsync(TenantRouteRequest req, CancellationToken ct)
    {
        var command = new DeactivateTenantCommand(req.TenantId);
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
