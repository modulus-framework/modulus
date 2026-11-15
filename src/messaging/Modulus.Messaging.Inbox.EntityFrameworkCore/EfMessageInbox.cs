using Microsoft.EntityFrameworkCore;

namespace Modulus.Messaging.Inbox.EntityFrameworkCore;

internal sealed class EfMessageInbox<TContext>(TContext db) : IMessageInbox
    where TContext : DbContext
{
    public async Task<bool> Claim(Guid messageId, string consumerKey, CancellationToken cancellationToken)
    {
        var handled = await db.Set<InboxRecord>()
            .AnyAsync(r => r.MessageId == messageId && r.ConsumerKey == consumerKey, cancellationToken)
            .ConfigureAwait(false);
        if (handled)
        {
            return false;
        }

        // Written with the consumer's changes. Two consumers racing on one message both reach here; the key
        // makes the second commit fail, its attempt rolls back, and the next delivery finds the record.
        db.Set<InboxRecord>().Add(new InboxRecord
        {
            MessageId = messageId,
            ConsumerKey = consumerKey,
            ReceivedAt = DateTime.UtcNow,
            TenantId = MessageTenant.Current,
        });
        return true;
    }
}
