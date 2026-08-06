using ModulusSample.Modules.Notifications.Domain.Events;
using ModulusSample.Modules.Notifications.Domain.ValueObjects;
using ModulusSample.Shared.Domain;
using Modulus.Core.Abstractions.Entities;

namespace ModulusSample.Modules.Notifications.Domain.Entities;

public sealed class Notification : AggregateRoot, IAuditableEntity
{
    public new NotificationId Id { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public string Title { get; private set; } = default!;
    public string Message { get; private set; } = default!;
    public NotificationType Type { get; private set; }
    public bool IsRead { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }
    public Guid TenantId { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public string? CreatedBy { get; private set; }
    public DateTime LastModifiedAt { get; private set; }
    public string? LastModifiedBy { get; private set; }

    private Notification() { }

    private Notification(
        NotificationId id,
        Guid recipientUserId,
        string title,
        string message,
        NotificationType type,
        Guid tenantId,
        string? createdBy)
    {
        base.Id = id.Value;
        Id = id;
        RecipientUserId = recipientUserId;
        Title = title;
        Message = message;
        Type = type;
        IsRead = false;
        TenantId = tenantId;
        CreatedAt = DateTime.UtcNow;
        CreatedBy = createdBy;
        LastModifiedAt = DateTime.UtcNow;
        LastModifiedBy = createdBy;

        Raise(new NotificationCreatedDomainEvent(id, recipientUserId, title, message, type, tenantId));
    }

    public static Result<Notification> Create(
        NotificationId id,
        Guid recipientUserId,
        string title,
        string message,
        NotificationType type,
        Guid tenantId,
        string? createdBy)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Result.Failure<Notification>(Error.Validation("Notification.EmptyTitle", "Title cannot be empty"));
        }

        if (title.Length > 255)
        {
            return Result.Failure<Notification>(Error.Validation("Notification.TooLongTitle", "Title cannot exceed 255 characters"));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return Result.Failure<Notification>(Error.Validation("Notification.EmptyMessage", "Message cannot be empty"));
        }

        return Result.Success(new Notification(id, recipientUserId, title.Trim(), message.Trim(), type, tenantId, createdBy));
    }

    public Result MarkAsRead(string modifiedBy)
    {
        if (IsRead)
        {
            return Result.Success();
        }

        IsRead = true;
        ReadAtUtc = DateTime.UtcNow;
        LastModifiedAt = DateTime.UtcNow;
        LastModifiedBy = modifiedBy;
        IncrementVersion();

        Raise(new NotificationReadDomainEvent(Id, RecipientUserId, TenantId));

        return Result.Success();
    }

    public void SetCreatedBy(string createdBy) => CreatedBy = createdBy;
    public void SetLastModifiedBy(string modifiedBy) => LastModifiedBy = modifiedBy;

    DateTime IAuditableEntity.CreatedAt { get => CreatedAt; set => CreatedAt = value; }
    string? IAuditableEntity.CreatedBy { get => CreatedBy; set => CreatedBy = value; }
    DateTime? IAuditableEntity.UpdatedAt { get => LastModifiedAt; set { if (value.HasValue) LastModifiedAt = value.Value; } }
    string? IAuditableEntity.UpdatedBy { get => LastModifiedBy; set => LastModifiedBy = value; }
}