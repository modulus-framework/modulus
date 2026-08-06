using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Tenants.Application.Tenants.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Tenants.Presentation.Tenants;

internal sealed class DeleteTenantEndpoint : Endpoint<DeleteTenantEndpoint.TenantRouteRequest>
{
    private readonly IMediator _mediator;

    public DeleteTenantEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Delete("/tenants/{tenantId}");
        Tag(Tags.Tenants);
        Summary("Delete a tenant (soft delete)");
    }

    public override async Task HandleAsync(TenantRouteRequest req, CancellationToken ct)
    {
        var command = new DeleteTenantCommand(req.TenantId);
        Result result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class TenantRouteRequest
    {
        public Guid TenantId { get; set; }
    }
}