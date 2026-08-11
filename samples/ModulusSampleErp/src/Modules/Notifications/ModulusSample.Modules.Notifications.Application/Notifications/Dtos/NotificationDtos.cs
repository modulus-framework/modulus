using ModulusSample.Modules.Notifications.Domain.ValueObjects;

namespace ModulusSample.Modules.Notifications.Application.Notifications.Dtos;

public sealed record NotificationResponse(
    Guid NotificationId,
    Guid RecipientUserId,
    string Title,
    string Message,
    NotificationType Type,
    bool IsRead,
    DateTime? ReadAtUtc,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime LastModifiedAt,
    string? LastModifiedBy);

public sealed record MarkAllReadResponse(int MarkedReadCount);

public sealed record UnreadCountResponse(long UnreadCount);
