using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Configuration.Application.Features.Commands;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Configuration.Presentation.Features;

internal sealed class DeleteFeatureFlagEndpoint : Endpoint<DeleteFeatureFlagEndpoint.DeleteFeatureFlagRequest>
{
    private readonly IMediator _mediator;

    public DeleteFeatureFlagEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Delete("/features/{featureFlagId}");
        Tag(Tags.Features);
        Summary("Delete a feature flag");
    }

    public override async Task HandleAsync(DeleteFeatureFlagRequest req, CancellationToken ct)
    {
        var command = new DeleteFeatureFlagCommand(req.FeatureFlagId);
        Result result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class DeleteFeatureFlagRequest
    {
        public Guid FeatureFlagId { get; set; }
    }
}
