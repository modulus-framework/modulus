using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Configuration.Application.Settings.Commands;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Configuration.Presentation.Settings;

internal sealed class BulkUpdateSettingsEndpoint : Endpoint<BulkUpdateSettingsEndpoint.BulkUpdateRequest, int>
{
    private readonly IMediator _mediator;

    public BulkUpdateSettingsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Put("/settings/bulk");
        Tag(Tags.Settings);
        Summary("Bulk update settings");
    }

    public override async Task HandleAsync(BulkUpdateRequest req, CancellationToken ct)
    {
        var command = new BulkUpdateSettingsCommand(req.SettingUpdates);
        Result<int> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class BulkUpdateRequest
    {
        public Dictionary<Guid, string> SettingUpdates { get; set; } = new();
    }
}
