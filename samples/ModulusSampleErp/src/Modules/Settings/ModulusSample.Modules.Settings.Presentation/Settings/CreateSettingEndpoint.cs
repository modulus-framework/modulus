using Modulus.AspNetCore.Endpoints;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Settings.Application.Settings.Commands;
using ModulusSample.Modules.Settings.Application.Settings.Dtos;
using ModulusSample.Shared.Application.Authorization;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Settings.Presentation.Settings;

internal sealed class CreateSettingEndpoint : Endpoint<CreateSettingEndpoint.CreateSettingRequest, CreateSettingResponse>
{
    private readonly IMediator _mediator;
    private readonly ICurrentTenant _currentTenant;

    public CreateSettingEndpoint(IMediator mediator, ICurrentTenant currentTenant)
    {
        _mediator = mediator;
        _currentTenant = currentTenant;
    }

    public override void Configure()
    {
        Post("/settings");
        Tag(Tags.Settings);
        Summary("Create a new setting");
    }

    public override async Task HandleAsync(CreateSettingRequest req, CancellationToken ct)
    {
        var command = new CreateSettingCommand(
            req.Key,
            req.Value,
            req.Category,
            req.Description,
            req.IsPublic,
            _currentTenant.TenantId ?? Guid.Empty);

        Result<CreateSettingResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/v1/settings/{result.Value.SettingId}", ct);
    }

    internal sealed class CreateSettingRequest
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
    }
}
