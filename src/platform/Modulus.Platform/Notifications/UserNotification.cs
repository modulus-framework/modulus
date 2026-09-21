namespace Modulus.Notifications;

/// <summary>
/// One persisted per-user notification (notification-center row).
/// Immutable: marking read produces a copy via <c>with</c>.
/// </summary>
public sealed record UserNotification
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid? TenantId { get; init; }

    public required Guid UserId { get; init; }

    public NotificationSeverity Severity { get; init; } = NotificationSeverity.Info;

    public required string Title { get; init; }

    public string? Message { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ReadAt { get; init; }

    public bool IsRead => ReadAt.HasValue;
}
