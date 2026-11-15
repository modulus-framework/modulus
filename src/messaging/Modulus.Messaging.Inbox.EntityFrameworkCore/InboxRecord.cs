namespace Modulus.Messaging.Inbox.EntityFrameworkCore;

/// <summary>A message one consumer has handled. Kept for the detection window, then purged.</summary>
public sealed class InboxRecord
{
    public Guid MessageId { get; set; }

    public string ConsumerKey { get; set; } = string.Empty;

    /// <summary>UTC time the consumer handled the message.</summary>
    public DateTime ReceivedAt { get; set; }
}
