using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Configuration.Application.Settings.Commands;
using TradeFlow.Modules.Configuration.Application.Settings.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Configuration.Presentation.Settings;

internal sealed class UpdateSettingValueEndpoint : Endpoint<UpdateSettingValueEndpoint.UpdateValueRequest, UpdateSettingResponse>
{
    private readonly IMediator _mediator;

    public UpdateSettingValueEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Put("/settings/{settingId}/value");
        Tag(Tags.Settings);
        Summary("Update a setting's value");
    }

    public override async Task HandleAsync(UpdateValueRequest req, CancellationToken ct)
    {
        var command = new UpdateSettingValueCommand(req.SettingId, req.NewValue);
        Result<UpdateSettingResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class UpdateValueRequest
    {
        public Guid SettingId { get; set; }
        public string NewValue { get; set; } = string.Empty;
    }
}
