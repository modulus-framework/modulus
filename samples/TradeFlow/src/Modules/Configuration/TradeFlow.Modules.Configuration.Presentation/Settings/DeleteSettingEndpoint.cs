using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Configuration.Application.Settings.Commands;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Configuration.Presentation.Settings;

internal sealed class DeleteSettingEndpoint : Endpoint<DeleteSettingEndpoint.DeleteSettingRequest>
{
    private readonly IMediator _mediator;

    public DeleteSettingEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Delete("/settings/{settingId}");
        Tag(Tags.Settings);
        Summary("Delete a setting");
    }

    public override async Task HandleAsync(DeleteSettingRequest req, CancellationToken ct)
    {
        var command = new DeleteSettingCommand(req.SettingId);
        Result result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class DeleteSettingRequest
    {
        public Guid SettingId { get; set; }
    }
}
