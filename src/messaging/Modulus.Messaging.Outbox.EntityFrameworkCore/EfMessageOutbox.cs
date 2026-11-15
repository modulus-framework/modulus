using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Modulus.Messaging.Outbox.EntityFrameworkCore;

/// <summary>Writes outgoing messages into the scope's DbContext. They commit or roll back with the caller's transaction.</summary>
internal sealed class EfMessageOutbox<TContext>(TContext db) : IMessageOutbox
    where TContext : DbContext
{
    public Task Add(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var type = envelope.Message.GetType();
        db.Set<OutboxRecord>().Add(new OutboxRecord
        {
            MessageId = envelope.MessageId,
            Endpoint = envelope.Endpoint,
            ConsumerKey = envelope.ConsumerKey,
            MessageType = type.AssemblyQualifiedName ?? type.FullName ?? type.Name,
            Body = JsonSerializer.Serialize(envelope.Message, type),
            Headers = JsonSerializer.Serialize(new Dictionary<string, string>(envelope.Headers, StringComparer.Ordinal)),
            EnqueuedAt = DateTime.UtcNow,
        });
        return Task.CompletedTask;
    }
}
