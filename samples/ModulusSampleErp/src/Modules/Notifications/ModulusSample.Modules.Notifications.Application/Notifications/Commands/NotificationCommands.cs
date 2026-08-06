using ModulusSample.Modules.Notifications.Application.Notifications.Dtos;
using ModulusSample.Modules.Notifications.Domain.ValueObjects;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Notifications.Application.Notifications.Commands;

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