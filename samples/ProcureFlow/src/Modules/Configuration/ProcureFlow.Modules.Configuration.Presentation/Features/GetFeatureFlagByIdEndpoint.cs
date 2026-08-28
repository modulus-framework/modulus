using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Configuration.Application.Features.Queries;
using ProcureFlow.Modules.Configuration.Application.Features.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Configuration.Presentation.Features;

internal sealed class GetFeatureFlagByIdEndpoint : Endpoint<GetFeatureFlagByIdEndpoint.GetByIdRequest, FeatureFlagResponse>
{
    private readonly IMediator _mediator;

    public GetFeatureFlagByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/features/{featureFlagId}");
        Tag(Tags.Features);
        Summary("Get a feature flag by ID");
    }

    public override async Task HandleAsync(GetByIdRequest req, CancellationToken ct)
    {
        var query = new GetFeatureFlagByIdQuery(req.FeatureFlagId);
        Result<FeatureFlagResponse> result = await _mediator.QueryAsync(query, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetByIdRequest
    {
        public Guid FeatureFlagId { get; set; }
    }
}
