using System.Text.Json;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Notifications.Application.Notifications.Commands;
using TradeFlow.Modules.Notifications.Application.Notifications.Dtos;
using TradeFlow.Modules.Notifications.Domain.Constants;
using TradeFlow.Modules.Notifications.Domain.Entities;
using TradeFlow.Modules.Notifications.Domain.Repositories;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Application.Notifications.Commands;

/// <summary>
/// Core notification engine: matches an event against subscription rules,
/// resolves recipients, applies user preferences, creates Notification + Log
/// rows per channel, and lets the outbox/dispatcher handle actual delivery.
/// </summary>
public sealed class ProcessNotificationEventCommandHandler(
    INotificationRuleRepository ruleRepository,
    INotificationPreferenceRepository preferenceRepository,
    INotificationRepository notificationRepository,
    INotificationLogRepository logRepository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<ProcessNotificationEventCommand, Result<ProcessEventResponse>>
{
    private const int MaxRecipientsPerRule = 500;

    public async Task<Result<ProcessEventResponse>> HandleAsync(ProcessNotificationEventCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;

        // 1. Match event against subscription rules
        var rules = await ruleRepository.GetByEventKeyAsync(request.EventKey, tenantId, ct);
        var enabledRules = rules.Where(r => r.Enabled).ToList();

        if (enabledRules.Count == 0)
            return Result.Success(new ProcessEventResponse(0, 0, 0));

        // 2. Parse event payload
        var payload = string.IsNullOrEmpty(request.PayloadJson)
            ? new Dictionary<string, object?>()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(request.PayloadJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

        int totalRecipients = 0;
        int totalNotifications = 0;

        foreach (var rule in enabledRules)
        {
            // 3. Resolve recipients from audience JSON
            var recipients = ResolveRecipients(rule.AudienceJson, payload, request.TriggerUserId);

            if (recipients.Count == 0)
                continue;

            // Cap recipients per rule
            if (recipients.Count > MaxRecipientsPerRule)
                recipients = recipients.Take(MaxRecipientsPerRule).ToList();

            totalRecipients += recipients.Count;

            // 4. Determine active channels (rule channels ∩ user preferences)
            foreach (var recipientId in recipients.Distinct())
            {
                var channels = await GetEffectiveChannelsAsync(rule, recipientId, tenantId, ct);

                if (channels == NotificationChannel.None)
                    continue;

                // 5. Create the in-app notification
                var title = RenderTitle(request.EventKey, payload);
                var message = RenderMessage(request.EventKey, payload);

                var notifResult = Notification.Create(
                    NotificationId.Create(), recipientId, title, message,
                    MapSeverityToNotificationType(rule.Severity), tenantId,
                    request.TriggerUserId?.ToString());

                if (notifResult.IsSuccess)
                {
                    await notificationRepository.AddAsync(notifResult.Value, ct);
                    totalNotifications++;

                    // 6. Create log entries per channel
                    foreach (var channel in Enum.GetValues<NotificationChannel>().Where(c => c != NotificationChannel.None && channels.HasFlag(c)))
                    {
                        var log = NotificationLog.CreateQueued(
                            NotificationLogId.Create(), tenantId,
                            notifResult.Value.Id.Value, request.EventKey,
                            recipientId, channel);
                        await logRepository.AddAsync(log, ct);
                    }
                }
            }
        }

        await unitOfWork.CommitAsync(ct);

        return Result.Success(new ProcessEventResponse(enabledRules.Count, totalRecipients, totalNotifications));
    }

    private async Task<NotificationChannel> GetEffectiveChannelsAsync(
        NotificationRule rule, Guid recipientUserId, Guid tenantId, CancellationToken ct)
    {
        var pref = await preferenceRepository.GetByUserAndCategoryAsync(
            recipientUserId, rule.EventKey, tenantId, ct);

        if (pref is null)
            return rule.Channels;

        // Intersection of rule channels and user preference channels
        return rule.Channels & pref.EnabledChannels;
    }

    private static List<Guid> ResolveRecipients(string audienceJson, Dictionary<string, object?> payload, Guid? triggerUserId)
    {
        var recipients = new List<Guid>();

        try
        {
            var audience = JsonSerializer.Deserialize<List<AudienceEntry>>(audienceJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (audience is null)
                return recipients;

            foreach (var entry in audience)
            {
                switch (entry.Type?.ToLowerInvariant())
                {
                    case "specificuser":
                    case "user":
                        if (Guid.TryParse(entry.Value?.ToString(), out var userId))
                            recipients.Add(userId);
                        break;

                    case "role":
                    case "position":
                    case "documentparticipants":
                    case "managerofinitiator":
                    case "headofdepartment":
                    case "headofbusinessunit":
                    case "headofcompany":
                        // These require external resolution services (OrgStructure module).
                        // For now, check if the audience JSON contains explicit user IDs.
                        if (entry.UserIds?.Count > 0)
                            recipients.AddRange(entry.UserIds);
                        break;
                }
            }

            // Fallback: if trigger user is provided and no explicit recipients, notify them
            if (recipients.Count == 0 && triggerUserId.HasValue)
                recipients.Add(triggerUserId.Value);
        }
        catch
        {
            // Malformed audience JSON — skip this rule silently
        }

        return recipients;
    }

    private static string RenderTitle(string eventKey, Dictionary<string, object?> payload)
    {
        if (payload.TryGetValue("title", out var titleObj) && titleObj is string title)
            return title;

        return $"Notification: {eventKey}";
    }

    private static string RenderMessage(string eventKey, Dictionary<string, object?> payload)
    {
        if (payload.TryGetValue("message", out var msgObj) && msgObj is string msg)
            return msg;

        if (payload.TryGetValue("body", out var bodyObj) && bodyObj is string body)
            return body;

        return $"Event '{eventKey}' occurred at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC";
    }

    private static NotificationType MapSeverityToNotificationType(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Critical => NotificationType.Error,
        NotificationSeverity.High => NotificationType.Warning,
        NotificationSeverity.Normal => NotificationType.Info,
        _ => NotificationType.Info
    };

    private sealed class AudienceEntry
    {
        public string? Type { get; set; }
        public object? Value { get; set; }
        public List<Guid>? UserIds { get; set; }
    }
}
