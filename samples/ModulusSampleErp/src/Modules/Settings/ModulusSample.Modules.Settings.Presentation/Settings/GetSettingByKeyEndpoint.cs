using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Settings.Application.Settings.Queries;
using ModulusSample.Modules.Settings.Application.Settings.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Settings.Presentation.Settings;

internal sealed class GetSettingByKeyEndpoint : Endpoint<GetSettingByKeyEndpoint.GetKeyRequest, SettingResponse>
{
    private readonly IMediator _mediator;

    public GetSettingByKeyEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/settings/key/{key}");
        Tag(Tags.Settings);
        Summary("Get a setting by key");
    }

    public override async Task HandleAsync(GetKeyRequest req, CancellationToken ct)
    {
        var query = new GetSettingByKeyQuery(req.Key);
        Result<SettingResponse> result = await _mediator.QueryAsync(query, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetKeyRequest
    {
        public string Key { get; set; } = string.Empty;
    }
}
