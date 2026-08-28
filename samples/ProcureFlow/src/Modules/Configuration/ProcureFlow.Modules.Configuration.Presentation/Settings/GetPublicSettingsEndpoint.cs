using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Configuration.Application.Settings.Queries;
using ProcureFlow.Modules.Configuration.Application.Settings.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Configuration.Presentation.Settings;

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
