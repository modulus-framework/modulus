using Modulus.AspNetCore.Endpoints;
using System.Text.Json;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Tenants.Application.Tenants.Commands;
using ModulusSample.Modules.Tenants.Application.Tenants.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Tenants.Presentation.Tenants;

internal sealed class CreateTenantEndpoint : Endpoint<CreateTenantEndpoint.CreateTenantRequest, CreateTenantResponse>
{
    private readonly IMediator _mediator;

    public CreateTenantEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/tenants");
        Tag(Tags.Tenants);
        Summary("Create a new tenant");
    }

    public override async Task HandleAsync(CreateTenantRequest req, CancellationToken ct)
    {
        var command = new CreateTenantCommand(
            req.Name,
            req.Subdomain,
            req.DatabaseConnectionString,
            req.Features,
            req.Settings);

        Result<CreateTenantResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/v1/tenants/{result.Value.TenantId}", ct);
    }

    internal sealed class CreateTenantRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Subdomain { get; set; } = string.Empty;
        public string DatabaseConnectionString { get; set; } = string.Empty;
        public JsonDocument? Features { get; set; }
        public JsonDocument? Settings { get; set; }
    }
}
