using ProcureFlow.Modules.Notifications.Application.Notifications.Dtos;
using ProcureFlow.Modules.Notifications.Domain.ValueObjects;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Notifications.Application.Notifications.Commands;

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
