using TradeFlow.Modules.Notifications.Application.Notifications.Dtos;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Application.Notifications.Commands;

public sealed record CreateNotificationCommand(
    Guid RecipientUserId,
    string Title,
    string Message,
    NotificationType Type,
    Guid TenantId) : Modulus.Mediator.Abstractions.ICommand<Result<NotificationResponse>>;

public sealed record MarkNotificationAsReadCommand(
    Guid NotificationId,
    Guid TenantId) : Modulus.Mediator.Abstractions.ICommand<Result<NotificationResponse>>;

public sealed record MarkAllNotificationsAsReadCommand(
    Guid TenantId) : Modulus.Mediator.Abstractions.ICommand<Result<MarkAllReadResponse>>;
