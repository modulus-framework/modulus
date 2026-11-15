using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Modulus.Messaging.Outbox.EntityFrameworkCore;

/// <summary>Writes outgoing messages into the scope's DbContext. They commit or roll back with the caller's transaction.</summary>
internal sealed class EfMessageOutbox<TContext>(TContext db) : IMessageOutbox
    where TContext : DbContext
{
    public Task Add(MessageEnvelope envelope, CancellationToken cancellationToken) => Insert(envelope, deliverAt: null, scheduleToken: null);

    public Task AddScheduled(MessageEnvelope envelope, DateTimeOffset deliverAt, Guid scheduleToken, CancellationToken cancellationToken) =>
        Insert(envelope, deliverAt.UtcDateTime, scheduleToken);

    public Task<bool> CancelScheduled(Guid scheduleToken, CancellationToken cancellationToken) =>
        CancelRowsAsync(scheduleToken, cancellationToken);

    private async Task<bool> CancelRowsAsync(Guid scheduleToken, CancellationToken cancellationToken)
    {
        // A claimed row (LockToken set) is already on its way out, so it is left alone.
        var deleted = await db.Set<OutboxRecord>()
            .Where(r => r.ScheduleToken == scheduleToken && r.LockToken == null)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        return deleted > 0;
    }

    private Task Insert(MessageEnvelope envelope, DateTime? deliverAt, Guid? scheduleToken)
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
            DeliverAt = deliverAt,
            ScheduleToken = scheduleToken,
        });
        return Task.CompletedTask;
    }
}
