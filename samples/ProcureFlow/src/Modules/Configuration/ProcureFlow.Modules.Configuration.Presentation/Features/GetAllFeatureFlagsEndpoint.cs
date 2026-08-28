using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Configuration.Application.Features.Queries;
using ProcureFlow.Modules.Configuration.Application.Features.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Configuration.Presentation.Features;

internal sealed class GetAllFeatureFlagsEndpoint : Endpoint<GetAllFeatureFlagsEndpoint.GetAllFeatureFlagsRequest, PagedResult<FeatureFlagResponse>>
{
    private readonly IMediator _mediator;

    public GetAllFeatureFlagsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/features");
        Tag(Tags.Features);
        Summary("Get all feature flags with optional filtering");
    }

    public override async Task HandleAsync(GetAllFeatureFlagsRequest req, CancellationToken ct)
    {
        var query = new GetAllFeatureFlagsQuery(req.IsEnabled, req.PageNumber, req.PageSize);
        Result<PagedResult<FeatureFlagResponse>> result = await _mediator.QueryAsync(query, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetAllFeatureFlagsRequest
    {
        public bool? IsEnabled { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}
