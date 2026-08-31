using TradeFlow.Modules.Notifications.Application.Notifications.Dtos;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Application.Notifications.Commands;

// ── Notification Rules ──

public sealed record CreateNotificationRuleCommand(
    string EventKey,
    string AudienceJson,
    NotificationChannel Channels,
    NotificationSeverity Severity,
    string? TemplateKey,
    string? ThrottleJson,
    bool Enabled = true) : Modulus.Mediator.Abstractions.ICommand<Result<NotificationRuleResponse>>;

public sealed record UpdateNotificationRuleCommand(
    Guid RuleId,
    string AudienceJson,
    NotificationChannel Channels,
    NotificationSeverity Severity,
    string? TemplateKey,
    string? ThrottleJson,
    bool Enabled) : Modulus.Mediator.Abstractions.ICommand<Result<NotificationRuleResponse>>;

public sealed record DeleteNotificationRuleCommand(
    Guid RuleId) : Modulus.Mediator.Abstractions.ICommand<Result>;

// ── Notification Templates ──

public sealed record CreateNotificationTemplateCommand(
    string TemplateKey,
    NotificationChannel Channel,
    string Locale,
    string Subject,
    string Body,
    string? VariablesJsonSchema) : Modulus.Mediator.Abstractions.ICommand<Result<NotificationTemplateResponse>>;

public sealed record UpdateNotificationTemplateCommand(
    Guid TemplateId,
    string Subject,
    string Body,
    string? VariablesJsonSchema) : Modulus.Mediator.Abstractions.ICommand<Result<NotificationTemplateResponse>>;

public sealed record DeleteNotificationTemplateCommand(
    Guid TemplateId) : Modulus.Mediator.Abstractions.ICommand<Result>;

// ── Notification Preferences ──

public sealed record CreateNotificationPreferenceCommand(
    Guid UserId,
    string EventCategory,
    NotificationChannel EnabledChannels,
    string? QuietHoursStart,
    string? QuietHoursEnd,
    string? TimeZoneId,
    string? DigestFrequency,
    string? Locale) : Modulus.Mediator.Abstractions.ICommand<Result<NotificationPreferenceResponse>>;

public sealed record UpdateNotificationPreferenceCommand(
    Guid PreferenceId,
    NotificationChannel EnabledChannels,
    string? QuietHoursStart,
    string? QuietHoursEnd,
    string? TimeZoneId,
    string? DigestFrequency,
    string? Locale) : Modulus.Mediator.Abstractions.ICommand<Result<NotificationPreferenceResponse>>;

// ── Event Processing (core engine) ──

public sealed record ProcessNotificationEventCommand(
    string EventKey,
    string? PayloadJson,
    Guid? TriggerUserId) : Modulus.Mediator.Abstractions.ICommand<Result<ProcessEventResponse>>;
