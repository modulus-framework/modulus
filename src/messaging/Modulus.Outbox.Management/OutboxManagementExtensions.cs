using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
            params DbContext[] contexts) =>
        {
            var p = page ?? 1;
            var ps = pageSize ?? 20;
            if (p < 1) p = 1;
            if (ps < 1 || ps > 1000) ps = 20;

            var allMessages = new List<OutboxMessage>();
            foreach (var db in contexts)
            {
                try
                {
                    var contextMessages = await db.Set<OutboxMessage>()
                        .Where(m => m.ProcessedAt == null && m.RetryCount >= 3) // Assuming MaxRetries=3 default
                        .AsNoTracking()
                        .ToListAsync(ct);
                    allMessages.AddRange(contextMessages);
                }
                catch (InvalidOperationException) when (
                    db.Model.FindEntityType(typeof(OutboxMessage)) == null)
                {
                    // This DbContext doesn't have an outbox table configured
                }
            }

            // Apply filters
            var query = allMessages.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(moduleFilter))
                query = query.Where(m => m.ModuleName.Contains(moduleFilter, StringComparison.OrdinalIgnoreCase));
            if (Guid.TryParse(tenantFilter, out var tenantId) && tenantId != Guid.Empty)
                query = query.Where(m => m.TenantId == tenantId);

            var sorted = query
                .OrderByDescending(m => m.CreatedAt)
                .ToList();

            var total = sorted.Count;
            var items = sorted
                .Skip((p - 1) * ps)
                .Take(ps)
                .Select(m => new OutboxDeadLetterListItem(
                    m.Id, m.MessageType, m.ModuleName, m.TenantId,
                    m.CreatedAt, m.RetryCount, m.Error))
                .ToList();

            return Results.Ok(new PaginatedResponse<OutboxDeadLetterListItem>(items, total, p, ps));
        })
        .WithSummary("List dead-lettered messages");

        // GET /outbox/dead-letters/{id}
        group.MapGet("/dead-letters/{id:guid}", async (
            Guid id,
            CancellationToken ct,
            params DbContext[] contexts) =>
        {
            foreach (var db in contexts)
            {
                try
                {
                    var message = await db.Set<OutboxMessage>()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(m => m.Id == id, ct);

                    if (message is not null && message.ProcessedAt == null && message.RetryCount >= 3)
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
        // POST /outbox/replay
        group.MapPost("/replay", async (
            OutboxReplayRequest request,
            ICurrentUser currentUser,
            CancellationToken ct,
            params DbContext[] contexts) =>
        {
            if (request.MessageIds is not { Length: > 0 })
                return Results.BadRequest("MessageIds must be non-empty");

            int replayedCount = 0, notFoundCount = 0, failedCount = 0;
            var failures = new List<string>();

            foreach (var messageId in request.MessageIds)
            {
                bool found = false;
                foreach (var db in contexts)
                {
                    try
                    {
                        var message = await db.Set<OutboxMessage>()
                            .FirstOrDefaultAsync(m => m.Id == messageId, ct);

                        if (message is not null && message.ProcessedAt == null)
                        {
                            found = true;
                            try
                            {
                                // Reset the message for retry: clear retry count, clear error,
                                // clear locks, set NextAttemptAt to now so it's picked up immediately.
                                message.RetryCount = 0;
                                message.Error = null;
                                message.LockedBy = null;
                                message.LockedUntil = null;
                                message.NextAttemptAt = DateTime.UtcNow;

                                await db.SaveChangesAsync(ct);
                                replayedCount++;
                            }
                            catch (Exception ex)
                            {
                                failedCount++;
                                failures.Add($"{messageId}: {ex.Message}");
                            }
                            break;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // DbContext doesn't have outbox table
                    }
                }

                if (!found)
                    notFoundCount++;
            }

            return Results.Ok(new OutboxReplayResponse(replayedCount, notFoundCount, failedCount, failures));
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
            params DbContext[] contexts) =>
        {
            var days = beforeDays ?? 30;
            if (days < 0) days = 0;

            var cutoff = DateTime.UtcNow.AddDays(-days);
            int purgedCount = 0;

            foreach (var db in contexts)
            {
                try
                {
                    purgedCount += await db.Set<OutboxMessage>()
                        .Where(m => m.ProcessedAt == null
                                 && m.RetryCount >= 3
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
