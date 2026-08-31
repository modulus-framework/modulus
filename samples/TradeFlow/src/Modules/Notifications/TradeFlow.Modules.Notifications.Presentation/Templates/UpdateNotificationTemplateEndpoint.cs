using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Notifications.Application.Notifications.Commands;
using TradeFlow.Modules.Notifications.Application.Notifications.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Presentation.Templates;

internal sealed class UpdateNotificationTemplateEndpoint(
    IMediator mediator) : Endpoint<UpdateNotificationTemplateEndpoint.UpdateTemplateRequest, NotificationTemplateResponse>
{
    public override void Configure()
    {
        Put("/notifications/templates/{TemplateId}");
        Tag(Tags.Notifications);
        Summary("Update a notification template");
    }

    public override async Task HandleAsync(UpdateTemplateRequest req, CancellationToken ct)
    {
        var command = new UpdateNotificationTemplateCommand(
            req.TemplateId, req.Subject, req.Body, req.VariablesJsonSchema);

        Result<NotificationTemplateResponse> result = await mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class UpdateTemplateRequest
    {
        public Guid TemplateId { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string? VariablesJsonSchema { get; set; }
    }
}
