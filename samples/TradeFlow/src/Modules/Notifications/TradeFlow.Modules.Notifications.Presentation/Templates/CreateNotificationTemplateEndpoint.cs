using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Notifications.Application.Notifications.Commands;
using TradeFlow.Modules.Notifications.Application.Notifications.Dtos;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Presentation.Templates;

internal sealed class CreateNotificationTemplateEndpoint(
    IMediator mediator) : Endpoint<CreateNotificationTemplateEndpoint.CreateTemplateRequest, NotificationTemplateResponse>
{
    public override void Configure()
    {
        Post("/notifications/templates");
        Tag(Tags.Notifications);
        Summary("Create a notification template");
    }

    public override async Task HandleAsync(CreateTemplateRequest req, CancellationToken ct)
    {
        var command = new CreateNotificationTemplateCommand(
            req.TemplateKey, req.Channel, req.Locale,
            req.Subject, req.Body, req.VariablesJsonSchema);

        Result<NotificationTemplateResponse> result = await mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/v1/notifications/templates/{result.Value.Id}", ct);
    }

    internal sealed class CreateTemplateRequest
    {
        public string TemplateKey { get; set; } = string.Empty;
        public NotificationChannel Channel { get; set; } = NotificationChannel.InApp;
        public string Locale { get; set; } = "en";
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string? VariablesJsonSchema { get; set; }
    }
}
