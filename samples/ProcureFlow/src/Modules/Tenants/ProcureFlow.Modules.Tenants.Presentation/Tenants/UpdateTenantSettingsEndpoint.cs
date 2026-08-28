using Modulus.AspNetCore.Endpoints;
using System.Text.Json;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Tenants.Application.Tenants.Commands;
using ProcureFlow.Modules.Tenants.Application.Tenants.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Tenants.Presentation.Tenants;

internal sealed class UpdateTenantSettingsEndpoint : Endpoint<UpdateTenantSettingsEndpoint.UpdateSettingsRequest, UpdateTenantResponse>
{
    private readonly IMediator _mediator;

    public UpdateTenantSettingsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Put("/tenants/{tenantId}/settings");
        Tag(Tags.Tenants);
        Summary("Update tenant settings");
    }

    public override async Task HandleAsync(UpdateSettingsRequest req, CancellationToken ct)
    {
        var command = new UpdateTenantSettingsCommand(req.TenantId, req.Settings);
        Result<UpdateTenantResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class UpdateSettingsRequest
    {
        public Guid TenantId { get; set; }
        public JsonDocument Settings { get; set; } = JsonDocument.Parse("{}");
    }
}
