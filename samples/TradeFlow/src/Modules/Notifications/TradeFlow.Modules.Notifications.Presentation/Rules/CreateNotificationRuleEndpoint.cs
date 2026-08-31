using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Notifications.Application.Notifications.Commands;
using TradeFlow.Modules.Notifications.Application.Notifications.Dtos;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Presentation.Rules;

internal sealed class CreateNotificationRuleEndpoint(
    IMediator mediator) : Endpoint<CreateNotificationRuleEndpoint.CreateRuleRequest, NotificationRuleResponse>
{
    public override void Configure()
    {
        Post("/notifications/rules");
        Tag(Tags.Notifications);
        Summary("Create a notification rule for an event");
    }

    public override async Task HandleAsync(CreateRuleRequest req, CancellationToken ct)
    {
        var command = new CreateNotificationRuleCommand(
            req.EventKey, req.AudienceJson, req.Channels,
            req.Severity, req.TemplateKey, req.ThrottleJson, req.Enabled);

        Result<NotificationRuleResponse> result = await mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/v1/notifications/rules/{result.Value.Id}", ct);
    }

    internal sealed class CreateRuleRequest
    {
        public string EventKey { get; set; } = string.Empty;
        public string AudienceJson { get; set; } = "[]";
        public NotificationChannel Channels { get; set; } = NotificationChannel.InApp;
        public NotificationSeverity Severity { get; set; } = NotificationSeverity.Normal;
        public string? TemplateKey { get; set; }
        public string? ThrottleJson { get; set; }
        public bool Enabled { get; set; } = true;
    }
}
