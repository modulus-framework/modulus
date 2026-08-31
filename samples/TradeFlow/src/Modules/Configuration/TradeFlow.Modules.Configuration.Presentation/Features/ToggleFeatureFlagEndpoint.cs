using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Configuration.Application.Features.Commands;
using TradeFlow.Modules.Configuration.Application.Features.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Configuration.Presentation.Features;

internal sealed class ToggleFeatureFlagEndpoint : Endpoint<ToggleFeatureFlagEndpoint.ToggleFeatureFlagRequest, UpdateFeatureFlagResponse>
{
    private readonly IMediator _mediator;

    public ToggleFeatureFlagEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Patch("/features/{featureFlagId}/toggle");
        Tag(Tags.Features);
        Summary("Enable or disable a feature flag");
    }

    public override async Task HandleAsync(ToggleFeatureFlagRequest req, CancellationToken ct)
    {
        var command = new ToggleFeatureFlagCommand(req.FeatureFlagId, req.IsEnabled);
        Result<UpdateFeatureFlagResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class ToggleFeatureFlagRequest
    {
        public Guid FeatureFlagId { get; set; }
        public bool IsEnabled { get; set; }
    }
}
