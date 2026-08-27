using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Settings.Application.Settings.Queries;
using ModulusSample.Modules.Settings.Application.Settings.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Settings.Presentation.Settings;

internal sealed class GetPublicSettingsEndpoint : EndpointWithoutRequest<List<PublicSettingResponse>>
{
    private readonly IMediator _mediator;

    public GetPublicSettingsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/settings/public");
        Tag(Tags.Settings);
        Summary("Get all public settings");
        AllowAnonymous();
    }

    protected override async Task HandleAsync(CancellationToken ct)
    {
        Result<List<PublicSettingResponse>> result = await _mediator.QueryAsync(new GetPublicSettingsQuery(), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
