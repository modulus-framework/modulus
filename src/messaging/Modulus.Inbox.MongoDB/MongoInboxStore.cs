namespace Modulus.Inbox.MongoDB;

using System.Text.Json;
using global::MongoDB.Driver;
using Modulus.Inbox.Abstractions;

/// <summary>
/// MongoDB document for the inbox (idempotency) collection.
/// </summary>
public sealed class MongoInboxMessage
{
    public Guid        Id            { get; init; }
    public string      MessageType   { get; init; } = default!;
    public string      Payload       { get; init; } = default!;
    public Guid        TenantId      { get; init; }
    public string      ModuleName    { get; init; } = default!;
    public DateTime    ReceivedAt    { get; init; } = DateTime.UtcNow;
    public DateTime?   ProcessedAt   { get; set;  }
    public InboxStatus Status        { get; set;  } = InboxStatus.Pending;
    public string?     Error         { get; set;  }
    public int         RetryCount    { get; set;  }
    public string?     CorrelationId { get; init; }
}

/// <summary>
/// MongoDB-backed inbox store for idempotent message processing.
/// Uses the integration event EventId as the _id for deduplication.
/// </summary>
internal sealed class MongoInboxStore(
    IMongoCollection<MongoInboxMessage> collection)
{
    /// <summary>
    /// Attempts to insert a new inbox record. Returns true if new (first delivery),
    /// false if the message was already seen (duplicate).
    /// </summary>
    public async Task<MongoInboxMessage?> TryEnlistAsync(
        InboxMessage message, CancellationToken ct)
    {
        var doc = new MongoInboxMessage
        {
            Id            = message.Id,
            MessageType   = message.MessageType,
            Payload       = message.Payload,
            TenantId      = message.TenantId,
            ModuleName    = message.ModuleName,
            CorrelationId = message.CorrelationId,
            ReceivedAt    = message.ReceivedAt,
            Status        = InboxStatus.Processing,
        };

        try
        {
            await collection.InsertOneAsync(doc, cancellationToken: ct);
            return doc;
        }
        catch (MongoWriteException ex) when (
            ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return null; // Already seen — duplicate delivery
        }
    }

    public async Task MarkProcessedAsync(Guid id, CancellationToken ct)
    {
        var filter = Builders<MongoInboxMessage>.Filter.Eq(x => x.Id, id);
        var update = Builders<MongoInboxMessage>.Update
            .Set(x => x.Status, InboxStatus.Processed)
            .Set(x => x.ProcessedAt, DateTime.UtcNow)
            .Set(x => x.RetryCount, 0);

        await collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    public async Task MarkFailedAsync(
        Guid id, string error, CancellationToken ct)
    {
        var filter = Builders<MongoInboxMessage>.Filter.Eq(x => x.Id, id);
        var update = Builders<MongoInboxMessage>.Update
            .Set(x => x.Status, InboxStatus.Failed)
            .Set(x => x.Error, error)
            .Inc(x => x.RetryCount, 1);

        await collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }
}
