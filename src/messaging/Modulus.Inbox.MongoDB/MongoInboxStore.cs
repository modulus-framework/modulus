namespace Modulus.Inbox.MongoDB;

using System.Text.Json;
using global::MongoDB.Driver;
using Modulus.Inbox.Abstractions;

/// <summary>
/// MongoDB document for the inbox (idempotency) collection.
/// </summary>
public sealed class MongoInboxMessage
{
    public Guid Id { get; init; }
    public string MessageType { get; init; } = default!;
    public string Payload { get; init; } = default!;
    public Guid TenantId { get; init; }
    public string ModuleName { get; init; } = default!;
    public DateTime ReceivedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public InboxStatus Status { get; set; } = InboxStatus.Pending;
    public string? Error { get; set; }
    public int RetryCount { get; set; }
    public string? CorrelationId { get; init; }
    public DateTime? ClaimedAt { get; set; }
}

/// <summary>
/// MongoDB-backed <see cref="IInboxStore"/> for idempotent message processing.
/// Uses the integration event EventId as the <c>_id</c> for deduplication;
/// the collection's unique PK index gives the same atomic-claim guarantee as
/// EF Core's PK constraint.
/// </summary>
internal sealed class MongoInboxStore(
    IMongoCollection<MongoInboxMessage> collection) : IInboxStore
{
    public async Task<InboxMessage?> TryClaimAsync(
        Guid eventId,
        string messageType,
        string payload,
        int maxRetries,
        TimeSpan claimTimeout,
        CancellationToken ct)
    {
        var existing = await collection
            .Find(x => x.Id == eventId)
            .FirstOrDefaultAsync(ct);

        if (existing is { Status: InboxStatus.Processed })
            return null;

        if (existing is { } prev && prev.RetryCount >= maxRetries)
            return null;

        var now = DateTime.UtcNow;

        if (existing is null)
        {
            // New record — insert atomically. A concurrent insert on the same
            // _id throws DuplicateKey → defer.
            var doc = new MongoInboxMessage
            {
                Id = eventId,
                MessageType = messageType,
                Payload = payload,
                ModuleName = collection.CollectionNamespace.CollectionName,
                ReceivedAt = now,
                Status = InboxStatus.Processing,
                ClaimedAt = now,
            };

            try
            {
                await collection.InsertOneAsync(doc, cancellationToken: ct);
            }
            catch (MongoWriteException ex) when (
                ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                throw new InboxDeferralException(
                    $"Inbox message {eventId} is being processed by another consumer.");
            }
        }
        else if (existing.Status == InboxStatus.Processing)
        {
            // Lease check: a claim held past the timeout means the claimant
            // crashed between claiming and persisting the final state. Reclaim
            // it (optimistically — the filter re-checks ClaimedAt) instead of
            // deferring forever. Legacy docs (ClaimedAt null) fall back to
            // ReceivedAt as the claim time.
            var claimedAt = existing.ClaimedAt ?? existing.ReceivedAt;
            if (now - claimedAt < claimTimeout)
                throw new InboxDeferralException(
                    $"Inbox message {eventId} is already being processed.");

            var reclaimFilter = Builders<MongoInboxMessage>.Filter.And(
                Builders<MongoInboxMessage>.Filter.Eq(x => x.Id, eventId),
                Builders<MongoInboxMessage>.Filter.Eq(x => x.Status, InboxStatus.Processing),
                Builders<MongoInboxMessage>.Filter.Eq(x => x.ClaimedAt, existing.ClaimedAt));
            var reclaimUpdate = Builders<MongoInboxMessage>.Update
                .Set(x => x.ClaimedAt, now)
                .Set(x => x.Error, (string?)null);

            var reclaimed = await collection.UpdateOneAsync(
                reclaimFilter, reclaimUpdate, cancellationToken: ct);

            if (reclaimed.MatchedCount == 0)
                throw new InboxDeferralException(
                    $"Inbox message {eventId} is being processed by another consumer.");
        }
        else
        {
            // Existing Pending/Failed — atomically transition to Processing.
            // The filter re-checks the status server-side so two consumers
            // that both read the same Failed row cannot both win: only the
            // first UPDATE matches; the second affects zero rows → defer.
            var filter = Builders<MongoInboxMessage>.Filter.And(
                Builders<MongoInboxMessage>.Filter.Eq(x => x.Id, eventId),
                Builders<MongoInboxMessage>.Filter.In(x => x.Status,
                    new[] { InboxStatus.Pending, InboxStatus.Failed }));
            var update = Builders<MongoInboxMessage>.Update
                .Set(x => x.Status, InboxStatus.Processing)
                .Set(x => x.ClaimedAt, now)
                .Set(x => x.Error, (string?)null);

            var result = await collection.UpdateOneAsync(filter, update, cancellationToken: ct);

            if (result.MatchedCount == 0)
                throw new InboxDeferralException(
                    $"Inbox message {eventId} is being processed by another consumer.");
        }

        return new InboxMessage
        {
            Id = eventId,
            MessageType = messageType,
            Payload = payload,
            Status = InboxStatus.Processing,
            ReceivedAt = now,
            ClaimedAt = now,
        };
    }

    public async Task MarkProcessedAsync(Guid id, CancellationToken ct)
    {
        var filter = Builders<MongoInboxMessage>.Filter.Eq(x => x.Id, id);
        var update = Builders<MongoInboxMessage>.Update
            .Set(x => x.Status, InboxStatus.Processed)
            .Set(x => x.ProcessedAt, DateTime.UtcNow)
            .Set(x => x.RetryCount, 0)
            .Set(x => x.Error, (string?)null)
            .Set(x => x.ClaimedAt, (DateTime?)null);

        await collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    public async Task MarkFailedAsync(Guid id, string error, CancellationToken ct)
    {
        var filter = Builders<MongoInboxMessage>.Filter.Eq(x => x.Id, id);
        var update = Builders<MongoInboxMessage>.Update
            .Set(x => x.Status, InboxStatus.Failed)
            .Set(x => x.Error, error)
            .Set(x => x.ClaimedAt, (DateTime?)null)
            .Inc(x => x.RetryCount, 1);

        await collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }
}
