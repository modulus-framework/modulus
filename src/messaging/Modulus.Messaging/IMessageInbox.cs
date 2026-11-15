namespace Modulus.Messaging;

/// <summary>
/// Records that a consumer has handled a message, inside the consume attempt's unit of work.
/// Returns false when this message was already handled by this consumer within the detection window (RULE-04),
/// in which case the consumer is not run again.
/// </summary>
public interface IMessageInbox
{
    Task<bool> Claim(Guid messageId, string consumerKey, CancellationToken cancellationToken);
}
