using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.Outbox.Abstractions;

namespace Modulus.Outbox.Management;

/// <summary>
/// Operational management HTTP API for the transactional outbox: list, inspect,
/// replay, and purge dead-lettered messages so operators can recover from
/// failures without redeploying.
///
/// Every endpoint requires the <see cref="ManagePermission"/> permission via the
/// framework's <c>:</c>-policy convention.
/// </summary>
public static class OutboxManagementExtensions
{
    /// <summary>The permission guarding every management endpoint.</summary>
    public const string ManagePermission = "messaging:manage";

    /// <summary>
    /// Declares the <see cref="ManagePermission"/> permission in the registry.
    /// Requires <c>AddModulusOutbox()</c> and EF Core <c>AddOutbox()</c> on at least
    /// one module's DbContext — the endpoints operate on the concrete EF stores.
    /// </summary>
    public static IServiceCollection AddModulusOutboxManagement(
        this IServiceCollection services)
    {
        // Full AddAuthorization (not just AddAuthorizationCore) is needed for
        // policy evaluation on endpoints.
        services.AddAuthorization();

        // Mutating endpoints resolve ICurrentUser to attribute audit events.
        // TryAdd so this package doesn't force a specific identity/auth backend.
        services.TryAddScoped<ICurrentUser, NullCurrentUser>();

        return services.AddPermissions("Modulus.Outbox", registry =>
            registry.Add(
                ManagePermission,
                "Manage outbox operations: list, inspect, and replay dead-lettered messages."));
    }

    /// <summary>
    /// Maps the dead-letter management endpoints under <paramref name="prefix"/>,
    /// all guarded by <see cref="ManagePermission"/>.
    /// Returns the group so hosts can attach further conventions (rate limits,
    /// OpenAPI tags, …).
    /// </summary>
    public static RouteGroupBuilder MapModulusOutboxManagement(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/outbox")
    {
        var group = endpoints.MapGroup(prefix)
            .RequireAuthorization(ManagePermission)
            .WithTags("Outbox Management");

        MapDeadLetters(group);
        MapReplay(group);
        MapPurge(group);

        return group;
    }

    private static void MapDeadLetters(RouteGroupBuilder group)
    {
        // GET /outbox/dead-letters?page=1&pageSize=20&moduleFilter=...&tenantFilter=...
        group.MapGet("/dead-letters", async (
            int? page,
            int? pageSize,
            string? moduleFilter,
            string? tenantFilter,
            CancellationToken ct,
            IOptions<OutboxOptions> options,
            params DbContext[] contexts) =>
        {
            var p = page ?? 1;
            var ps = pageSize ?? 20;
            if (p < 1) p = 1;
            if (ps < 1 || ps > 1000) ps = 20;

            var maxRetries = options.Value.MaxRetries;
            // Bounded per-context fetch: filters pushed to the DB, each context
            // contributes at most p*ps rows so memory stays bounded even under a
            // failure storm (instead of loading every DLQ row into memory).
            var candidates = new List<OutboxMessage>();
            var total = 0;
            foreach (var db in contexts)
            {
                try
                {
                    if (db.Model.FindEntityType(typeof(OutboxMessage)) is null)
                        continue;
                    var q = db.Set<OutboxMessage>()
                        .Where(m => m.ProcessedAt == null && m.RetryCount >= maxRetries);
                    if (!string.IsNullOrWhiteSpace(moduleFilter))
                        q = q.Where(m => m.ModuleName.Contains(moduleFilter));
                    if (Guid.TryParse(tenantFilter, out var tenantId) && tenantId != Guid.Empty)
                        q = q.Where(m => m.TenantId == tenantId);

                    total += await q.CountAsync(ct);
                    var rows = await q
                        .OrderByDescending(m => m.CreatedAt)
                        .Take(p * ps)
                        .AsNoTracking()
                        .ToListAsync(ct);
                    candidates.AddRange(rows);
                }
                catch (InvalidOperationException) when (
                    db.Model.FindEntityType(typeof(OutboxMessage)) == null)
                {
                    // This DbContext doesn't have an outbox table configured
                }
            }

            var sorted = candidates
                .OrderByDescending(m => m.CreatedAt)
                .ToList();

            var totalCapped = total;
            var items = sorted
                .Skip((p - 1) * ps)
                .Take(ps)
                .Select(m => new OutboxDeadLetterListItem(
                    m.Id, m.MessageType, m.ModuleName, m.TenantId,
                    m.CreatedAt, m.RetryCount, m.Error))
                .ToList();

            return Results.Ok(new PaginatedResponse<OutboxDeadLetterListItem>(items, totalCapped, p, ps));
        })
        .WithSummary("List dead-lettered messages");

        // GET /outbox/dead-letters/{id}
        group.MapGet("/dead-letters/{id:guid}", async (
            Guid id,
            CancellationToken ct,
            IOptions<OutboxOptions> options,
            params DbContext[] contexts) =>
        {
            var maxRetries = options.Value.MaxRetries;
            foreach (var db in contexts)
            {
                try
                {
                    var message = await db.Set<OutboxMessage>()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(m => m.Id == id, ct);

                    if (message is not null && message.ProcessedAt == null && message.RetryCount >= maxRetries)
                        return Results.Ok(new OutboxDeadLetterDetail(
                            message.Id, message.MessageType, message.Payload,
                            message.ModuleName, message.TenantId,
                            message.CreatedAt, message.RetryCount, message.Error,
                            message.CorrelationId, message.CausationId));
                }
                catch (InvalidOperationException)
                {
                    // DbContext doesn't have outbox table
                }
            }

            return Results.NotFound();
        })
        .WithSummary("Get dead-lettered message details");
    }

    private static void MapReplay(RouteGroupBuilder group)
    {
        // POST /outbox/replay — only dead-lettered rows (Retry>=Max, unprocessed).
        // History is preserved: Error is archived into the audit log with the
        // acting user instead of being nulled. Batch ExecuteUpdate per context.
        group.MapPost("/replay", async (
            OutboxReplayRequest request,
            ICurrentUser currentUser,
            ILoggerFactory loggerFactory,
            IOptions<OutboxOptions> options,
            CancellationToken ct,
            params DbContext[] contexts) =>
        {
            if (request.MessageIds is not { Length: > 0 })
                return Results.BadRequest("MessageIds must be non-empty");

            var logger = loggerFactory.CreateLogger("Modulus.Outbox.Management");
            var maxRetries = options.Value.MaxRetries;
            var ids = request.MessageIds.Distinct().ToArray();
            var remaining = new HashSet<Guid>(ids);
            int replayedCount = 0, failedCount = 0;
            var failures = new List<string>();

            foreach (var db in contexts)
            {
                if (remaining.Count == 0)
                    break;
                try
                {
                    if (db.Model.FindEntityType(typeof(OutboxMessage)) is null)
                        continue;

                    // Audit history before reset (Error would otherwise be lost).
                    var doomed = await db.Set<OutboxMessage>()
                        .Where(m => remaining.Contains(m.Id)
                                 && m.ProcessedAt == null
                                 && m.RetryCount >= maxRetries)
                        .Select(m => new { m.Id, m.MessageType, m.Error, m.RetryCount })
                        .AsNoTracking()
                        .ToListAsync(ct);

                    var replayedIds = doomed.Select(d => d.Id).ToArray();
                    if (replayedIds.Length == 0)
                        continue;

                    await db.Set<OutboxMessage>()
                        .Where(m => replayedIds.Contains(m.Id))
                        .ExecuteUpdateAsync(
                            s => s.SetProperty(m => m.RetryCount, 0)
                                  .SetProperty(m => m.Error, (string?)null)
                                  .SetProperty(m => m.LockedBy, (string?)null)
                                  .SetProperty(m => m.LockedUntil, (DateTime?)null)
                                  .SetProperty(m => m.NextAttemptAt, DateTime.UtcNow),
                            ct);

                    foreach (var d in doomed)
                    {
                        remaining.Remove(d.Id);
                        replayedCount++;
                        logger.LogInformation(
                            "Outbox message {Id} ({Type}) replayed by {User}; archived error after {N} attempts: {Error}",
                            d.Id, d.MessageType, currentUser.UserId?.ToString() ?? "unknown", d.RetryCount, d.Error);
                    }
                }
                catch (Exception ex)
                {
                    failedCount += remaining.Count;
                    failures.Add($"context {db.GetType().Name}: {ex.Message}");
                    break;
                }
            }

            return Results.Ok(new OutboxReplayResponse(replayedCount, remaining.Count, failedCount, failures));
        })
        .WithSummary("Replay dead-lettered messages");
    }

    private static void MapPurge(RouteGroupBuilder group)
    {
        // DELETE /outbox/dead-letters/purge?beforeDays=30
        group.MapDelete("/dead-letters/purge", async (
            int? beforeDays,
            ICurrentUser currentUser,
            CancellationToken ct,
            IOptions<OutboxOptions> options,
            params DbContext[] contexts) =>
        {
            var days = beforeDays ?? 30;
            if (days < 0) days = 0;

            var cutoff = DateTime.UtcNow.AddDays(-days);
            var maxRetries = options.Value.MaxRetries;
            int purgedCount = 0;

            foreach (var db in contexts)
            {
                try
                {
                    purgedCount += await db.Set<OutboxMessage>()
                        .Where(m => m.ProcessedAt == null
                                 && m.RetryCount >= maxRetries
                                 && m.CreatedAt < cutoff)
                        .ExecuteDeleteAsync(ct);
                }
                catch (InvalidOperationException)
                {
                    // DbContext doesn't have outbox table
                }
            }

            return Results.Ok(new OutboxPurgeResponse(purgedCount));
        })
        .WithSummary("Purge old dead-lettered messages");
    }
}
