namespace Modulus.Messaging.Inbox.EntityFrameworkCore;

/// <summary>A message one consumer has handled. Kept for the detection window, then purged.</summary>
public sealed class InboxRecord
{
    public Guid MessageId { get; set; }

    public string ConsumerKey { get; set; } = string.Empty;

    /// <summary>UTC time the consumer handled the message.</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>
    /// The company the message was handled for (D7), or null when it had none. It lets a company's inbox rows be found for
    /// erase and audit. It is written from <c>MessageTenant.Current</c> in the consume attempt.
    /// </summary>
    public string? TenantId { get; set; }
}
