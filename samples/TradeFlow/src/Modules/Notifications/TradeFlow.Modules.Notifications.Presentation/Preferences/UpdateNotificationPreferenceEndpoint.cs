using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Notifications.Application.Notifications.Commands;
using TradeFlow.Modules.Notifications.Application.Notifications.Dtos;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Presentation.Preferences;

internal sealed class UpdateNotificationPreferenceEndpoint(
    IMediator mediator) : Endpoint<UpdateNotificationPreferenceEndpoint.UpdatePreferenceRequest, NotificationPreferenceResponse>
{
    public override void Configure()
    {
        Put("/notifications/preferences/{PreferenceId}");
        Tag(Tags.Notifications);
        Summary("Update notification preferences");
    }

    public override async Task HandleAsync(UpdatePreferenceRequest req, CancellationToken ct)
    {
        var command = new UpdateNotificationPreferenceCommand(
            req.PreferenceId, req.EnabledChannels,
            req.QuietHoursStart, req.QuietHoursEnd, req.TimeZoneId,
            req.DigestFrequency, req.Locale);

        Result<NotificationPreferenceResponse> result = await mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class UpdatePreferenceRequest
    {
        public Guid PreferenceId { get; set; }
        public NotificationChannel EnabledChannels { get; set; } = NotificationChannel.InApp | NotificationChannel.Email;
        public string? QuietHoursStart { get; set; }
        public string? QuietHoursEnd { get; set; }
        public string? TimeZoneId { get; set; }
        public string? DigestFrequency { get; set; }
        public string? Locale { get; set; }
    }
}
