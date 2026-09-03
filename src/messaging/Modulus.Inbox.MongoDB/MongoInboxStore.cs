namespace Modulus.Inbox.MongoDB;

using global::MongoDB.Driver;
using Modulus.Inbox.Abstractions;

/// <summary>
/// MongoDB document for the inbox (idempotency) collection.
/// </summary>
public sealed class MongoInboxMessage
{
    /// <summary>
    /// The document's <c>_id</c>. For rows written by the current schema this
    /// is an opaque random id (uniqueness of the real claim key —
    /// <see cref="EventId"/> + <see cref="HandlerName"/> — comes from a
    /// separate unique index, since a single event can have more than one
    /// handler and therefore more than one row).
    /// <para>
    /// For rows written before <see cref="EventId"/>/<see cref="HandlerName"/>
    /// existed ("legacy" rows), this is the integration event's EventId — the
    /// old scheme's key — and is how <see cref="MongoInboxStore"/> recognises
    /// and adopts them. See <see cref="Modulus.Inbox.Abstractions.InboxMessage.HandlerName"/>.
    /// </para>
    /// </summary>
    public Guid Id { get; init; }

    public Guid EventId { get; init; }

    /// <summary>Null on legacy rows (the field did not exist before this schema).</summary>
    public string? HandlerName { get; set; }

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
/// Atomicity comes from a unique compound index on
/// <c>(EventId, HandlerName)</c> (see <see cref="Extensions.InboxIndexInitializer"/>):
/// a concurrent insert for the same pair races and the loser gets a
/// <c>DuplicateKey</c> write error.
/// </summary>
internal sealed class MongoInboxStore(
    IMongoCollection<MongoInboxMessage> collection) : IInboxStore
{
    public async Task<InboxMessage?> TryClaimAsync(
        Guid eventId,
        string handlerName,
        string messageType,
        string payload,
        int maxRetries,
        TimeSpan claimTimeout,
        CancellationToken ct)
    {
        var existing = await collection
            .Find(x => x.EventId == eventId && x.HandlerName == handlerName)
            .FirstOrDefaultAsync(ct);

        var now = DateTime.UtcNow;

        if (existing is null)
        {
            // No row for THIS handler yet. Before inserting a fresh one, check
            // for a legacy doc (written before EventId/HandlerName existed —
            // _id == eventId, HandlerName missing/null) for the same event: it
            // was claimed under the old single-key scheme and must be honoured
            // for every handler so an in-flight upgrade neither reprocesses an
            // already-handled event nor drops one still mid-flight.
            var legacy = await collection
                .Find(x => x.Id == eventId && x.HandlerName == null)
                .FirstOrDefaultAsync(ct);

            if (legacy is { Status: InboxStatus.Processed })
                return null;

            if (legacy is { } prevLegacy && prevLegacy.RetryCount >= maxRetries)
                return null;

            if (legacy is { Status: InboxStatus.Processing } processingLegacy)
            {
                var claimedAt = processingLegacy.ClaimedAt ?? processingLegacy.ReceivedAt;
                if (now - claimedAt < claimTimeout)
                    throw new InboxDeferralException(
                        $"Inbox message {eventId} is already being processed.");

                // Lease expired. Adopt the legacy doc for this handler instead
                // of leaving it wedged forever. Optimistic: the filter
                // re-checks ClaimedAt so a concurrent adopter/renewer cannot be
                // usurped; if we lose the race, fall through to a fresh insert
                // below — adoption already renamed the legacy doc's claim key
                // out from under it, so our own insert cannot conflict.
                var adoptFilter = Builders<MongoInboxMessage>.Filter.And(
                    Builders<MongoInboxMessage>.Filter.Eq(x => x.Id, processingLegacy.Id),
                    Builders<MongoInboxMessage>.Filter.Eq(x => x.Status, InboxStatus.Processing),
                    Builders<MongoInboxMessage>.Filter.Eq(x => x.ClaimedAt, processingLegacy.ClaimedAt));
                var adoptUpdate = Builders<MongoInboxMessage>.Update
                    .Set(x => x.EventId, eventId)
                    .Set(x => x.HandlerName, handlerName)
                    .Set(x => x.ClaimedAt, now)
                    .Set(x => x.Error, (string?)null);

                var adopted = await collection.UpdateOneAsync(adoptFilter, adoptUpdate, cancellationToken: ct);
                if (adopted.MatchedCount == 1)
                    return new InboxMessage
                    {
                        Id = eventId,
                        HandlerName = handlerName,
                        MessageType = processingLegacy.MessageType,
                        Payload = processingLegacy.Payload,
                        Status = InboxStatus.Processing,
                        ReceivedAt = processingLegacy.ReceivedAt,
                        RetryCount = processingLegacy.RetryCount,
                        ClaimedAt = now,
                    };
                // lost the race — fall through to a fresh insert.
            }
            else if (legacy is { } pendingOrFailedLegacy)
            {
                // Pending/Failed legacy doc, under the retry budget: adopt it
                // for this handler. Two handlers racing on the same legacy doc
                // cannot both win: only the first UPDATE matches (filter
                // re-checks HandlerName == null server-side); the loser falls
                // through to its own fresh insert below.
                var adoptFilter = Builders<MongoInboxMessage>.Filter.And(
                    Builders<MongoInboxMessage>.Filter.Eq(x => x.Id, pendingOrFailedLegacy.Id),
                    Builders<MongoInboxMessage>.Filter.Eq(x => x.HandlerName, (string?)null),
                    Builders<MongoInboxMessage>.Filter.In(x => x.Status,
                        new[] { InboxStatus.Pending, InboxStatus.Failed }));
                var adoptUpdate = Builders<MongoInboxMessage>.Update
                    .Set(x => x.EventId, eventId)
                    .Set(x => x.HandlerName, handlerName)
                    .Set(x => x.Status, InboxStatus.Processing)
                    .Set(x => x.ClaimedAt, now)
                    .Set(x => x.Error, (string?)null);

                var adopted = await collection.UpdateOneAsync(adoptFilter, adoptUpdate, cancellationToken: ct);
                if (adopted.MatchedCount == 1)
                    return new InboxMessage
                    {
                        Id = eventId,
                        HandlerName = handlerName,
                        MessageType = pendingOrFailedLegacy.MessageType,
                        Payload = pendingOrFailedLegacy.Payload,
                        Status = InboxStatus.Processing,
                        ReceivedAt = pendingOrFailedLegacy.ReceivedAt,
                        RetryCount = pendingOrFailedLegacy.RetryCount,
                        ClaimedAt = now,
                    };
                // lost the race — fall through to a fresh insert.
            }

            // New record — insert atomically. A concurrent insert for the same
            // (EventId, HandlerName) races on the unique compound index; the
            // loser gets a DuplicateKey write error → defer.
            var doc = new MongoInboxMessage
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                HandlerName = handlerName,
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

            return new InboxMessage
            {
                Id = eventId,
                HandlerName = handlerName,
                MessageType = messageType,
                Payload = payload,
                Status = InboxStatus.Processing,
                ReceivedAt = now,
                ClaimedAt = now,
            };
        }

        if (existing is { Status: InboxStatus.Processed })
            return null;

        if (existing is { } prev && prev.RetryCount >= maxRetries)
            return null;

        if (existing.Status == InboxStatus.Processing)
        {
            // Lease check: a claim held past the timeout means the claimant
            // crashed between claiming and persisting the final state. Reclaim
            // it (optimistically — the filter re-checks ClaimedAt) instead of
            // deferring forever.
            var claimedAt = existing.ClaimedAt ?? existing.ReceivedAt;
            if (now - claimedAt < claimTimeout)
                throw new InboxDeferralException(
                    $"Inbox message {eventId} is already being processed.");

            var reclaimFilter = Builders<MongoInboxMessage>.Filter.And(
                Builders<MongoInboxMessage>.Filter.Eq(x => x.EventId, eventId),
                Builders<MongoInboxMessage>.Filter.Eq(x => x.HandlerName, handlerName),
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
                Builders<MongoInboxMessage>.Filter.Eq(x => x.EventId, eventId),
                Builders<MongoInboxMessage>.Filter.Eq(x => x.HandlerName, handlerName),
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
            HandlerName = handlerName,
            MessageType = existing.MessageType,
            Payload = existing.Payload,
            Status = InboxStatus.Processing,
            ReceivedAt = existing.ReceivedAt,
            RetryCount = existing.RetryCount,
            ClaimedAt = now,
        };
    }

    public async Task MarkProcessedAsync(Guid eventId, string handlerName, CancellationToken ct)
    {
        var filter = Builders<MongoInboxMessage>.Filter.And(
            Builders<MongoInboxMessage>.Filter.Eq(x => x.EventId, eventId),
            Builders<MongoInboxMessage>.Filter.Eq(x => x.HandlerName, handlerName));
        var update = Builders<MongoInboxMessage>.Update
            .Set(x => x.Status, InboxStatus.Processed)
            .Set(x => x.ProcessedAt, DateTime.UtcNow)
            .Set(x => x.RetryCount, 0)
            .Set(x => x.Error, (string?)null)
            .Set(x => x.ClaimedAt, (DateTime?)null);

        await collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    public async Task MarkFailedAsync(Guid eventId, string handlerName, string error, CancellationToken ct)
    {
        var filter = Builders<MongoInboxMessage>.Filter.And(
            Builders<MongoInboxMessage>.Filter.Eq(x => x.EventId, eventId),
            Builders<MongoInboxMessage>.Filter.Eq(x => x.HandlerName, handlerName));
        var update = Builders<MongoInboxMessage>.Update
            .Set(x => x.Status, InboxStatus.Failed)
            .Set(x => x.Error, error)
            .Set(x => x.ClaimedAt, (DateTime?)null)
            .Inc(x => x.RetryCount, 1);

        await collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }
}
