using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Notifications.Application.Notifications.Commands;
using ProcureFlow.Modules.Notifications.Application.Notifications.Dtos;
using ProcureFlow.Modules.Notifications.Domain.ValueObjects;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Notifications.Presentation.Preferences;

internal sealed class CreateNotificationPreferenceEndpoint(
    IMediator mediator) : Endpoint<CreateNotificationPreferenceEndpoint.CreatePreferenceRequest, NotificationPreferenceResponse>
{
    public override void Configure()
    {
        Post("/notifications/preferences");
        Tag(Tags.Notifications);
        Summary("Create or update notification preferences for a user");
    }

    public override async Task HandleAsync(CreatePreferenceRequest req, CancellationToken ct)
    {
        var command = new CreateNotificationPreferenceCommand(
            req.UserId, req.EventCategory, req.EnabledChannels,
            req.QuietHoursStart, req.QuietHoursEnd, req.TimeZoneId,
            req.DigestFrequency, req.Locale);

        Result<NotificationPreferenceResponse> result = await mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/v1/notifications/preferences/{result.Value.Id}", ct);
    }

    internal sealed class CreatePreferenceRequest
    {
        public Guid UserId { get; set; }
        public string EventCategory { get; set; } = string.Empty;
        public NotificationChannel EnabledChannels { get; set; } = NotificationChannel.InApp | NotificationChannel.Email;
        public string? QuietHoursStart { get; set; }
        public string? QuietHoursEnd { get; set; }
        public string? TimeZoneId { get; set; }
        public string? DigestFrequency { get; set; }
        public string? Locale { get; set; }
    }
}
