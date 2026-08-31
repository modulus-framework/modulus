using TradeFlow.Modules.Notifications.Application.Notifications.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Application.Notifications.Queries;

// ── Notification Rules ──

public sealed record GetNotificationRuleByIdQuery(
    Guid RuleId) : Modulus.Mediator.Abstractions.IQuery<Result<NotificationRuleResponse>>;

public sealed record GetNotificationRulesQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<NotificationRuleResponse>>>;

public sealed record GetNotificationRulesByEventKeyQuery(
    string EventKey) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<NotificationRuleResponse>>>;

// ── Notification Templates ──

public sealed record GetNotificationTemplateByIdQuery(
    Guid TemplateId) : Modulus.Mediator.Abstractions.IQuery<Result<NotificationTemplateResponse>>;

public sealed record GetNotificationTemplatesQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<NotificationTemplateResponse>>>;

// ── Notification Preferences ──

public sealed record GetNotificationPreferenceByIdQuery(
    Guid PreferenceId) : Modulus.Mediator.Abstractions.IQuery<Result<NotificationPreferenceResponse>>;

public sealed record GetMyNotificationPreferencesQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<NotificationPreferenceResponse>>>;

// ── Notification Logs ──

public sealed record GetNotificationLogsByNotificationQuery(
    Guid NotificationId) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<NotificationLogResponse>>>;

public sealed record GetFailedNotificationLogsQuery(
    int PageNumber = 1,
    int PageSize = 20) : Modulus.Mediator.Abstractions.IQuery<Result<PagedResult<NotificationLogResponse>>>;
