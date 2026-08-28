using ProcureFlow.Modules.Notifications.Domain.ValueObjects;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Notifications.Domain.Entities;

/// <summary>
/// Append-only delivery log for each (notification × channel × recipient).
/// Tracks Queued → Sending → Sent → Delivered → Read (or Failed → Retrying → DeadLettered).
/// Provider receipts (message IDs, cost, latency) are stored for analytics.
/// </summary>
public sealed class NotificationLog : AggregateRoot
{
    private NotificationLog() { }

    internal NotificationLog(
        NotificationLogId id,
        Guid tenantId,
        Guid? notificationId,
        string eventKey,
        Guid recipientUserId,
        NotificationChannel channel,
        NotificationLogStatus status,
        string? providerMessageId,
        string? providerResponse,
        string? errorMessage,
        int retryCount,
        DateTime? nextRetryAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        NotificationId = notificationId;
        EventKey = eventKey;
        RecipientUserId = recipientUserId;
        Channel = channel;
        Status = status;
        ProviderMessageId = providerMessageId;
        ProviderResponse = providerResponse;
        ErrorMessage = errorMessage;
        RetryCount = retryCount;
        NextRetryAtUtc = nextRetryAtUtc;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public new NotificationLogId Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? NotificationId { get; private set; }
    public string EventKey { get; private set; } = null!;
    public Guid RecipientUserId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public NotificationLogStatus Status { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string? ProviderResponse { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int RetryCount { get; private set; }
    public DateTime? NextRetryAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public DateTime? DeliveredAtUtc { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static NotificationLog CreateQueued(
        NotificationLogId id,
        Guid tenantId,
        Guid? notificationId,
        string eventKey,
        Guid recipientUserId,
        NotificationChannel channel)
    {
        return new NotificationLog(id, tenantId, notificationId, eventKey, recipientUserId, channel,
            NotificationLogStatus.Queued, null, null, null, 0, null);
    }

    public void MarkSending()
    {
        Status = NotificationLogStatus.Sending;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkSent(string? providerMessageId = null, string? providerResponse = null)
    {
        Status = NotificationLogStatus.Sent;
        ProviderMessageId = providerMessageId;
        ProviderResponse = providerResponse;
        SentAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkDelivered(string? providerResponse = null)
    {
        Status = NotificationLogStatus.Delivered;
        ProviderResponse = providerResponse;
        DeliveredAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkRead()
    {
        Status = NotificationLogStatus.Read;
        ReadAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailed(string? errorMessage = null)
    {
        Status = NotificationLogStatus.Failed;
        ErrorMessage = errorMessage;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkRetrying(DateTime nextRetryAtUtc)
    {
        RetryCount++;
        Status = NotificationLogStatus.Retrying;
        NextRetryAtUtc = nextRetryAtUtc;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkDeadLettered(string? errorMessage = null)
    {
        Status = NotificationLogStatus.DeadLettered;
        ErrorMessage = errorMessage;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
