using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Features.Application.Features.Queries;
using ModulusSample.Modules.Features.Application.Features.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Features.Presentation.Features;

internal sealed class GetEnabledFeatureFlagsEndpoint : EndpointWithoutRequest<IReadOnlyList<FeatureFlagResponse>>
{
    private readonly IMediator _mediator;

    public GetEnabledFeatureFlagsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/features/enabled");
        Tag(Tags.Features);
        Summary("Get all enabled feature flags for the current tenant");
        AllowAnonymous();
    }

    protected override async Task HandleAsync(CancellationToken ct)
    {
        Result<IReadOnlyList<FeatureFlagResponse>> result = await _mediator.QueryAsync(new GetEnabledFeatureFlagsQuery(), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
