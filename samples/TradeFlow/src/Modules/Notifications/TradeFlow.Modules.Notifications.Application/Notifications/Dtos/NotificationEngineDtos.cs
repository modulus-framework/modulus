using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Application.Notifications.Dtos;

public sealed record NotificationRuleResponse(
    Guid Id,
    Guid TenantId,
    string EventKey,
    string AudienceJson,
    NotificationChannel Channels,
    NotificationSeverity Severity,
    string? TemplateKey,
    string? ThrottleJson,
    bool Enabled,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record NotificationTemplateResponse(
    Guid Id,
    Guid TenantId,
    string TemplateKey,
    NotificationChannel Channel,
    string Locale,
    string Subject,
    string Body,
    string? VariablesJsonSchema,
    int Version,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record NotificationPreferenceResponse(
    Guid Id,
    Guid TenantId,
    Guid UserId,
    string EventCategory,
    NotificationChannel EnabledChannels,
    bool IsMandatory,
    string? QuietHoursStart,
    string? QuietHoursEnd,
    string? TimeZoneId,
    string? DigestFrequency,
    string? Locale,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record NotificationLogResponse(
    Guid Id,
    Guid TenantId,
    Guid? NotificationId,
    string EventKey,
    Guid RecipientUserId,
    NotificationChannel Channel,
    NotificationLogStatus Status,
    string? ProviderMessageId,
    string? ProviderResponse,
    string? ErrorMessage,
    int RetryCount,
    DateTime? NextRetryAtUtc,
    DateTime? SentAtUtc,
    DateTime? DeliveredAtUtc,
    DateTime? ReadAtUtc,
    DateTime CreatedAtUtc);

public sealed record ProcessEventResponse(
    int MatchedRules,
    int RecipientsResolved,
    int NotificationsCreated);
