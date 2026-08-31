using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Notifications.Application.Notifications.Commands;
using TradeFlow.Modules.Notifications.Application.Notifications.Dtos;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Presentation.Rules;

internal sealed class UpdateNotificationRuleEndpoint(
    IMediator mediator) : Endpoint<UpdateNotificationRuleEndpoint.UpdateRuleRequest, NotificationRuleResponse>
{
    public override void Configure()
    {
        Put("/notifications/rules/{RuleId}");
        Tag(Tags.Notifications);
        Summary("Update a notification rule");
    }

    public override async Task HandleAsync(UpdateRuleRequest req, CancellationToken ct)
    {
        var command = new UpdateNotificationRuleCommand(
            req.RuleId, req.AudienceJson, req.Channels,
            req.Severity, req.TemplateKey, req.ThrottleJson, req.Enabled);

        Result<NotificationRuleResponse> result = await mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class UpdateRuleRequest
    {
        public Guid RuleId { get; set; }
        public string AudienceJson { get; set; } = "[]";
        public NotificationChannel Channels { get; set; } = NotificationChannel.InApp;
        public NotificationSeverity Severity { get; set; } = NotificationSeverity.Normal;
        public string? TemplateKey { get; set; }
        public string? ThrottleJson { get; set; }
        public bool Enabled { get; set; } = true;
    }
}
