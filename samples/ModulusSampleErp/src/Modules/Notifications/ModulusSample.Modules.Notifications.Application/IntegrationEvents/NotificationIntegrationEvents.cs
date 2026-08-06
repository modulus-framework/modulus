using Modulus.Events.Abstractions;
using ModulusSample.Modules.Notifications.Domain.ValueObjects;

namespace ModulusSample.Modules.Notifications.Application.IntegrationEvents;

public sealed record NotificationCreatedIntegrationEvent(
    Guid NotificationId,
    Guid RecipientUserId,
    string Title,
    string Message,
    NotificationType Type,
    Guid TenantId) : IntegrationEventBase("Notifications.NotificationCreated.v1");

public sealed record NotificationReadIntegrationEvent(
    Guid NotificationId,
    Guid RecipientUserId,
    Guid TenantId) : IntegrationEventBase("Notifications.NotificationRead.v1");