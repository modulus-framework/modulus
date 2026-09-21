namespace Modulus.Outbox.MongoDB;

using global::MongoDB.Driver;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Outbox.Abstractions;
using Modulus.Outbox.Management;

/// <summary>
/// MongoDB mirror of the EF outbox management API (see
/// <c>Modulus.Outbox.Management.OutboxManagementExtensions</c>): list, inspect,
/// replay, and purge dead-lettered documents. Reuses the same record models and
/// the same <c>messaging:manage</c> permission. Register via
/// <c>AddModulusOutboxManagement()</c> for permissions, then map this group.
/// </summary>
public static class MongoOutboxManagementExtensions
{
    public static RouteGroupBuilder MapModulusMongoOutboxManagement(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/outbox")
    {
        var group = endpoints.MapGroup(prefix)
            .RequireAuthorization(OutboxManagementExtensions.ManagePermission)
            .WithTags("Outbox Management");

        group.MapGet("/dead-letters", async (
            int? page,
            int? pageSize,
            string? moduleFilter,
            string? tenantFilter,
            CancellationToken ct,
            IOptions<OutboxOptions> options,
            IMongoCollection<MongoOutboxMessage> collection) =>
        {
            var p = page is < 1 ? 1 : page ?? 1;
            var ps = pageSize is < 1 or > 1000 ? 20 : pageSize ?? 20;
            var maxRetries = options.Value.MaxRetries;

            var filter = Builders<MongoOutboxMessage>.Filter.And(
                Builders<MongoOutboxMessage>.Filter.Eq(m => m.ProcessedAt, null),
                Builders<MongoOutboxMessage>.Filter.Gte(m => m.RetryCount, maxRetries),
                string.IsNullOrWhiteSpace(moduleFilter)
                    ? Builders<MongoOutboxMessage>.Filter.Empty
                    : Builders<MongoOutboxMessage>.Filter.Regex(m => m.ModuleName,
                        new global::MongoDB.Bson.BsonRegularExpression(moduleFilter, "i")),
                Guid.TryParse(tenantFilter, out var tenantId) && tenantId != Guid.Empty
                    ? Builders<MongoOutboxMessage>.Filter.Eq(m => m.TenantId, tenantId)
                    : Builders<MongoOutboxMessage>.Filter.Empty);

            var total = (int)await collection.CountDocumentsAsync(filter, cancellationToken: ct);
            var rows = await collection.Find(filter)
                .SortByDescending(m => m.CreatedAt)
                .Skip((p - 1) * ps)
                .Limit(ps)
                .ToListAsync(ct);

            var items = rows.Select(m => new OutboxDeadLetterListItem(
                m.Id, m.MessageType, m.ModuleName, m.TenantId,
                m.CreatedAt, m.RetryCount, m.Error)).ToList();
            return Results.Ok(new PaginatedResponse<OutboxDeadLetterListItem>(items, total, p, ps));
        }).WithSummary("List dead-lettered messages (Mongo)");

        group.MapGet("/dead-letters/{id:guid}", async (
            Guid id,
            CancellationToken ct,
            IOptions<OutboxOptions> options,
            IMongoCollection<MongoOutboxMessage> collection) =>
        {
            var maxRetries = options.Value.MaxRetries;
            var doc = await collection.Find(m => m.Id == id).FirstOrDefaultAsync(ct);
            if (doc is null || doc.ProcessedAt is not null || doc.RetryCount < maxRetries)
                return Results.NotFound();
            return Results.Ok(new OutboxDeadLetterDetail(
                doc.Id, doc.MessageType, doc.Payload, doc.ModuleName, doc.TenantId,
                doc.CreatedAt, doc.RetryCount, doc.Error,
                doc.CorrelationId, doc.CausationId));
        }).WithSummary("Get dead-lettered message details (Mongo)");

        group.MapPost("/replay", async (
            OutboxReplayRequest request,
            ICurrentUser currentUser,
            ILoggerFactory loggerFactory,
            IOptions<OutboxOptions> options,
            CancellationToken ct,
            IMongoCollection<MongoOutboxMessage> collection) =>
        {
            if (request.MessageIds is not { Length: > 0 })
                return Results.BadRequest("MessageIds must be non-empty");

            var logger = loggerFactory.CreateLogger("Modulus.Outbox.Management");
            var maxRetries = options.Value.MaxRetries;
            var ids = request.MessageIds.Distinct().ToArray();

            var doomed = await collection.Find(
                Builders<MongoOutboxMessage>.Filter.And(
                    Builders<MongoOutboxMessage>.Filter.In(m => m.Id, ids),
                    Builders<MongoOutboxMessage>.Filter.Eq(m => m.ProcessedAt, null),
                    Builders<MongoOutboxMessage>.Filter.Gte(m => m.RetryCount, maxRetries)))
                .ToListAsync(ct);

            if (doomed.Count == 0)
                return Results.Ok(new OutboxReplayResponse(0, ids.Length, 0, []));

            var replayedIds = doomed.Select(d => d.Id).ToArray();
            await collection.UpdateManyAsync(
                Builders<MongoOutboxMessage>.Filter.In(m => m.Id, replayedIds),
                Builders<MongoOutboxMessage>.Update
                    .Set(m => m.RetryCount, 0)
                    .Set(m => m.Error, (string?)null)
                    .Set(m => m.LockedBy, (string?)null)
                    .Set(m => m.LockedUntil, (DateTime?)null)
                    .Set(m => m.NextAttemptAt, DateTime.UtcNow),
                cancellationToken: ct);

            foreach (var d in doomed)
                logger.LogInformation(
                    "Outbox message {Id} ({Type}) replayed by {User}; archived error after {N} attempts: {Error}",
                    d.Id, d.MessageType, currentUser.UserId?.ToString() ?? "unknown", d.RetryCount, d.Error);

            return Results.Ok(new OutboxReplayResponse(doomed.Count, ids.Length - doomed.Count, 0, []));
        }).WithSummary("Replay dead-lettered messages (Mongo)");

        group.MapDelete("/dead-letters/purge", async (
            int? beforeDays,
            CancellationToken ct,
            IOptions<OutboxOptions> options,
            IMongoCollection<MongoOutboxMessage> collection) =>
        {
            var days = beforeDays ?? 30;
            if (days < 0)
                days = 0;
            var cutoff = DateTime.UtcNow.AddDays(-days);
            var maxRetries = options.Value.MaxRetries;
            var result = await collection.DeleteManyAsync(
                Builders<MongoOutboxMessage>.Filter.And(
                    Builders<MongoOutboxMessage>.Filter.Eq(m => m.ProcessedAt, null),
                    Builders<MongoOutboxMessage>.Filter.Gte(m => m.RetryCount, maxRetries),
                    Builders<MongoOutboxMessage>.Filter.Lt(m => m.CreatedAt, cutoff)),
                cancellationToken: ct);
            return Results.Ok(new OutboxPurgeResponse((int)result.DeletedCount));
        }).WithSummary("Purge old dead-lettered messages (Mongo)");

        return group;
    }
}
