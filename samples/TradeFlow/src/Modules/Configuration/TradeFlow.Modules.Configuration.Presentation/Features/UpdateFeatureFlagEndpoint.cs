using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Configuration.Application.Features.Commands;
using TradeFlow.Modules.Configuration.Application.Features.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Configuration.Presentation.Features;

internal sealed class UpdateFeatureFlagEndpoint : Endpoint<UpdateFeatureFlagEndpoint.UpdateFeatureFlagRequest, UpdateFeatureFlagResponse>
{
    private readonly IMediator _mediator;

    public UpdateFeatureFlagEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Put("/features/{featureFlagId}");
        Tag(Tags.Features);
        Summary("Update a feature flag's name and description");
    }

    public override async Task HandleAsync(UpdateFeatureFlagRequest req, CancellationToken ct)
    {
        var command = new UpdateFeatureFlagCommand(req.FeatureFlagId, req.Name, req.Description);
        Result<UpdateFeatureFlagResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class UpdateFeatureFlagRequest
    {
        public Guid FeatureFlagId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
}
