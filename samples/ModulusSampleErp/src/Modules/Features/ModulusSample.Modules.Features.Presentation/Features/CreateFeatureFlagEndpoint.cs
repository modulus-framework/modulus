using Modulus.AspNetCore.Endpoints;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Features.Application.Features.Commands;
using ModulusSample.Modules.Features.Application.Features.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Features.Presentation.Features;

internal sealed class CreateFeatureFlagEndpoint : Endpoint<CreateFeatureFlagEndpoint.CreateFeatureFlagRequest, CreateFeatureFlagResponse>
{
    private readonly IMediator _mediator;
    private readonly ICurrentTenant _currentTenant;

    public CreateFeatureFlagEndpoint(IMediator mediator, ICurrentTenant currentTenant)
    {
        _mediator = mediator;
        _currentTenant = currentTenant;
    }

    public override void Configure()
    {
        Post("/features");
        Tag(Tags.Features);
        Summary("Create a new feature flag");
    }

    public override async Task HandleAsync(CreateFeatureFlagRequest req, CancellationToken ct)
    {
        var command = new CreateFeatureFlagCommand(
            req.Key,
            req.Name,
            req.Description,
            req.IsEnabled,
            _currentTenant.TenantId ?? Guid.Empty);

        Result<CreateFeatureFlagResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/v1/features/{result.Value.FeatureFlagId}", ct);
    }

    internal sealed class CreateFeatureFlagRequest
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsEnabled { get; set; }
    }
}