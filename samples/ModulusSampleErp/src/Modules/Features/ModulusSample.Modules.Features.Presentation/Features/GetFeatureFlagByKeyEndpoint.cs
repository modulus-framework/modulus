using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Features.Application.Features.Queries;
using ModulusSample.Modules.Features.Application.Features.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Features.Presentation.Features;

internal sealed class GetFeatureFlagByKeyEndpoint : Endpoint<GetFeatureFlagByKeyEndpoint.GetByKeyRequest, FeatureFlagResponse>
{
    private readonly IMediator _mediator;

    public GetFeatureFlagByKeyEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/features/key/{key}");
        Tag(Tags.Features);
        Summary("Get a feature flag by key");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetByKeyRequest req, CancellationToken ct)
    {
        var query = new GetFeatureFlagByKeyQuery(req.Key);
        Result<FeatureFlagResponse> result = await _mediator.QueryAsync(query, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetByKeyRequest
    {
        public string Key { get; set; } = string.Empty;
    }
}
