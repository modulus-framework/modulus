namespace Modulus.Outbox.MongoDB;

using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Outbox.Abstractions;

/// <summary>
/// Polls the MongoDB outbox collection, atomically claims rows, dispatches
/// them via <see cref="IOutboxDispatcher"/>, and applies exponential backoff
/// retries on failure. Mirrors <c>OutboxProcessor</c> (EF Core) but uses
/// MongoDB's server-side filters for atomic claiming.
/// </summary>
public sealed class MongoOutboxProcessor(
    IMongoCollection<MongoOutboxMessage> collection,
    IServiceProvider sp,
    IOptions<OutboxOptions> opts,
    ILogger<MongoOutboxProcessor> logger)
{
    // Unique id per processor instance, used to claim rows so concurrent
    // instances (or concurrent polls) don't dispatch the same message twice.
    private readonly string _instanceId = Guid.NewGuid().ToString("N");

    private const int PurgeBatchSize = 1000;

    public async Task ProcessAsync(CancellationToken ct = default)
    {
        await using var scope = sp.CreateAsyncScope();
        var ssp = scope.ServiceProvider;
        var dispatcher = ssp.GetRequiredService<IOutboxDispatcher>();
        var currentTenant = ssp.GetService<ICurrentTenant>();
        var correlation = ssp.GetService<ICorrelationContext>();
        var options = opts.Value;
        var now = DateTime.UtcNow;
        var lockUntil = now.AddSeconds(options.LockTimeoutSec);

        // Housekeeping FIRST: a quiet system exits at the empty-candidates
        // short-circuit below, so purge must run before it or dispatched /
        // dead-lettered documents would accumulate forever once steady state
        // is reached. Mirrors OutboxProcessor (EF Core).
        await PurgeExpiredAsync(collection, options, ct);

        // 1. Pick candidate ids: unprocessed, under the retry budget, not
        //    currently locked by another instance, and whose backoff (if any)
        //    has elapsed.
        var candidateFilter = Builders<MongoOutboxMessage>.Filter.And(
            Builders<MongoOutboxMessage>.Filter.Eq(m => m.ProcessedAt, null),
            Builders<MongoOutboxMessage>.Filter.Lt(m => m.RetryCount, options.MaxRetries),
            Builders<MongoOutboxMessage>.Filter.Or(
                Builders<MongoOutboxMessage>.Filter.Eq(m => m.LockedUntil, null),
                Builders<MongoOutboxMessage>.Filter.Lt(m => m.LockedUntil, now)),
            Builders<MongoOutboxMessage>.Filter.Or(
                Builders<MongoOutboxMessage>.Filter.Eq(m => m.NextAttemptAt, null),
                Builders<MongoOutboxMessage>.Filter.Lte(m => m.NextAttemptAt, now)));

        var candidateIds = await collection
            .Find(candidateFilter)
            .SortBy(m => m.CreatedAt)
            .Limit(options.BatchSize)
            .Project(m => m.Id)
            .ToListAsync(ct);

        if (candidateIds.Count == 0) return;

        // 2. Atomically claim those rows. The filter re-checks ProcessedAt and
        //    LockedUntil server-side, so two instances that both picked the
        //    same candidates cannot both win — the writes serialize per doc
        //    and the second affects zero rows.
        var claimFilter = Builders<MongoOutboxMessage>.Filter.And(
            Builders<MongoOutboxMessage>.Filter.In(m => m.Id, candidateIds),
            Builders<MongoOutboxMessage>.Filter.Eq(m => m.ProcessedAt, null),
            Builders<MongoOutboxMessage>.Filter.Or(
                Builders<MongoOutboxMessage>.Filter.Eq(m => m.LockedUntil, null),
                Builders<MongoOutboxMessage>.Filter.Lt(m => m.LockedUntil, now)));
        var claimUpdate = Builders<MongoOutboxMessage>.Update
            .Set(m => m.LockedBy, _instanceId)
            .Set(m => m.LockedUntil, lockUntil);

        await collection.UpdateManyAsync(claimFilter, claimUpdate, cancellationToken: ct);

        // 3. Load only the rows this instance claimed in THIS batch (scoped to
        //    candidateIds, mirroring OutboxProcessor). Without this scope,
        //    rows locked by this instance in a prior cycle that crashed before
        //    their status update would also be returned, causing duplicate
        //    dispatch if the processor instance were ever reused.
        var ownedFilter = Builders<MongoOutboxMessage>.Filter.And(
            Builders<MongoOutboxMessage>.Filter.In(m => m.Id, candidateIds),
            Builders<MongoOutboxMessage>.Filter.Eq(m => m.LockedBy, _instanceId),
            Builders<MongoOutboxMessage>.Filter.Eq(m => m.ProcessedAt, null));

        var messages = await collection
            .Find(ownedFilter)
            .SortBy(m => m.CreatedAt)
            .ToListAsync(ct);

        if (messages.Count == 0) return;

        // 4. Dispatch each. Dispatch is an irreversible side effect, so we
        //    mark ProcessedAt only AFTER success.
        foreach (var message in messages)
        {
            // Restore the tenant context captured when the outbox row was
            // written so downstream handlers see the correct tenant. A row
            // written in the host context (TenantId == Guid.Empty) dispatches
            // under an explicit host scope (Change(null)) so fail-closed tenant
            // filters don't hide the host's own data from the handler.
            IDisposable? tenantScope = currentTenant is null
                ? null
                : currentTenant.Change(message.TenantId == Guid.Empty
                    ? null
                    : new TenantInfo(message.TenantId, message.TenantId.ToString("N")));

            // Restore the originating correlation id so it flows into the
            // in-process handlers and onto the broker envelope for cross-service
            // tracing (mirrors OutboxProcessor).
            IDisposable? correlationScope =
                correlation is not null && !string.IsNullOrEmpty(message.CorrelationId)
                    ? correlation.BeginScope(message.CorrelationId)
                    : null;

            try
            {
                await dispatcher.DispatchAsync(ToOutboxMessage(message), ct);

                var doneFilter = Builders<MongoOutboxMessage>.Filter.Eq(m => m.Id, message.Id);
                var doneUpdate = Builders<MongoOutboxMessage>.Update
                    .Set(m => m.ProcessedAt, now)
                    .Set(m => m.LockedBy, (string?)null)
                    .Set(m => m.LockedUntil, (DateTime?)null);
                await collection.UpdateOneAsync(doneFilter, doneUpdate, cancellationToken: ct);

                logger.LogDebug("Outbox dispatched {Id} ({Type})",
                    message.Id, message.MessageType);
            }
            catch (Exception ex)
            {
                var newRetry = message.RetryCount + 1;
                var nextAttempt = now.AddSeconds(
                    Math.Min(options.InitialBackoffSec
                              * Math.Pow(2, newRetry), 3600));

                var errFilter = Builders<MongoOutboxMessage>.Filter.Eq(m => m.Id, message.Id);
                var errUpdate = Builders<MongoOutboxMessage>.Update
                    .Set(m => m.RetryCount, newRetry)
                    .Set(m => m.Error, ex.Message)
                    .Set(m => m.LockedBy, (string?)null)
                    .Set(m => m.LockedUntil, (DateTime?)null)
                    .Set(m => m.NextAttemptAt, nextAttempt);
                await collection.UpdateOneAsync(errFilter, errUpdate, cancellationToken: ct);

                if (newRetry >= options.MaxRetries)
                    logger.LogError(ex,
                        "Outbox message {Id} ({Type}) dead-lettered after {N} attempts.",
                        message.Id, message.MessageType, newRetry);
                else
                    logger.LogWarning(ex,
                        "Outbox dispatch failed for {Id} (attempt {N}); next attempt at {Next}.",
                        message.Id, newRetry, nextAttempt);
            }
            finally
            {
                correlationScope?.Dispose();
                tenantScope?.Dispose();
            }
        }
    }

    /// <summary>
    /// Bounded housekeeping delete of documents past their retention window:
    /// dispatched (ProcessedAt set) and dead-lettered (RetryCount exhausted)
    /// rows older than PurgeAfterDays. The id-probe + DeleteMany two-step
    /// keeps each pass bounded to <see cref="PurgeBatchSize"/> documents.
    /// </summary>
    private static async Task PurgeExpiredAsync(
        IMongoCollection<MongoOutboxMessage> collection,
        OutboxOptions options,
        CancellationToken ct)
    {
        if (options.PurgeAfterDays <= 0)
            return;

        var cutoff = DateTime.UtcNow.AddDays(-options.PurgeAfterDays);

        var expiredFilter = Builders<MongoOutboxMessage>.Filter.Or(
            Builders<MongoOutboxMessage>.Filter.And(
                Builders<MongoOutboxMessage>.Filter.Ne(m => m.ProcessedAt, null),
                Builders<MongoOutboxMessage>.Filter.Lt(m => m.ProcessedAt!, cutoff)),
            Builders<MongoOutboxMessage>.Filter.And(
                Builders<MongoOutboxMessage>.Filter.Eq(m => m.ProcessedAt, null),
                Builders<MongoOutboxMessage>.Filter.Gte(m => m.RetryCount, options.MaxRetries),
                Builders<MongoOutboxMessage>.Filter.Lt(m => m.CreatedAt, cutoff)));

        var expiredIds = await collection
            .Find(expiredFilter)
            .Limit(PurgeBatchSize)
            .Project(m => m.Id)
            .ToListAsync(ct);
        if (expiredIds.Count == 0)
            return;

        await collection.DeleteManyAsync(
            Builders<MongoOutboxMessage>.Filter.In(m => m.Id, expiredIds),
            cancellationToken: ct);
    }

    private static OutboxMessage ToOutboxMessage(MongoOutboxMessage m) => new()
    {
        Id = m.Id,
        MessageType = m.MessageType,
        Payload = m.Payload,
        TenantId = m.TenantId,
        ModuleName = m.ModuleName,
        CreatedAt = m.CreatedAt,
        RetryCount = m.RetryCount,
        CorrelationId = m.CorrelationId,
        CausationId = m.CausationId,
    };
}
