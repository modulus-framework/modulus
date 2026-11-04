namespace Modulus.Analytics.Management;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Modulus.Analytics.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;

/// <summary>
/// Operations API for analytics delivery: see how the queue stands, list, inspect, replay and purge dead-lettered records, and start and
/// watch backfill runs, so an operator can recover from a failed sink or load history without a redeploy. Every endpoint needs <see cref="ManagePermission"/>.
/// </summary>
public static class AnalyticsManagementExtensions
{
    /// <summary>The permission guarding every management endpoint.</summary>
    public const string ManagePermission = "analytics:manage";

    private const int MaxReplayIds = 1000;
    private const int MaxBackfillTenants = 1000;

    /// <summary>
    /// Declares <see cref="ManagePermission"/> (critical: it needs a step-up sign-in in production and cannot come from a wildcard grant).
    /// Needs <c>AddModulusAnalyticsStore&lt;TContext&gt;()</c>, because the endpoints operate on the delivery store.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddModulusAnalyticsManagement(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAuthorization();
        services.TryAddScoped<ICurrentUser, NullCurrentUser>();
        services.TryAddSingleton(TimeProvider.System);

        return services.AddPermissions("Modulus.Analytics", registry =>
            registry.Add(
                ManagePermission,
                "Manage analytics delivery: see queue status, list, replay and purge dead-lettered records, and start and list backfill runs.",
                null,
                PermissionSensitivity.Critical));
    }

    /// <summary>Maps the endpoints under <paramref name="prefix"/>, all guarded by <see cref="ManagePermission"/>.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="prefix">The route prefix. Defaults to <c>/analytics</c>.</param>
    /// <returns>The group, so hosts can attach further conventions.</returns>
    public static RouteGroupBuilder MapModulusAnalyticsManagement(this IEndpointRouteBuilder endpoints, string prefix = "/analytics")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix)
            .RequireAuthorization(ManagePermission)
            .WithTags("Analytics Management");

        // GET /analytics/status
        group.MapGet("/status", async (ModulusAnalyticsDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var rows = db.AnalyticsDeliveries.AsNoTracking();
            var pending = await rows.CountAsync(d => d.DeliveredAt == null && d.DeadLetteredAt == null, ct);
            var delivered = await rows.CountAsync(d => d.DeliveredAt != null, ct);
            var dead = await rows.CountAsync(d => d.DeadLetteredAt != null, ct);
            DateTime? oldest = pending == 0
                ? null
                : await rows.Where(d => d.DeliveredAt == null && d.DeadLetteredAt == null).MinAsync(d => (DateTime?)d.CreatedAt, ct);
            double? age = oldest is { } o ? Math.Max(0, (clock.GetUtcNow().UtcDateTime - o).TotalSeconds) : null;
            return Results.Ok(new AnalyticsQueueStatus(pending, delivered, dead, oldest, age));
        })
        .WithSummary("Show how the analytics delivery queue stands");

        // GET /analytics/freshness
        group.MapGet("/freshness", async (IAnalyticsFreshnessReader reader, CancellationToken ct)
            => Results.Ok(await reader.ReadAsync(ct)))
        .WithSummary("Show each dataset's lag against its freshness SLA");

        // GET /analytics/dead-letters?page=1&pageSize=20&dataset=...&tenant=...
        group.MapGet("/dead-letters", async (
            int? page, int? pageSize, string? dataset, string? tenant, ModulusAnalyticsDbContext db, CancellationToken ct) =>
        {
            var p = Math.Max(1, page ?? 1);
            var ps = pageSize is >= 1 and <= 1000 ? pageSize.Value : 20;
            var query = db.AnalyticsDeliveries.AsNoTracking().Where(d => d.DeadLetteredAt != null);
            if (!string.IsNullOrWhiteSpace(dataset))
            {
                query = query.Where(d => d.Dataset == dataset);
            }

            if (Guid.TryParse(tenant, out var tenantId) && tenantId != Guid.Empty)
            {
                query = query.Where(d => d.TenantId == tenantId);
            }

            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(d => d.DeadLetteredAt).ThenBy(d => d.Id).Skip((p - 1) * ps).Take(ps).ToListAsync(ct);
            return Results.Ok(new AnalyticsPage<AnalyticsDeadLetter>([.. rows.Select(ToModel)], total, p, ps));
        })
        .WithSummary("List dead-lettered analytics records");

        // GET /analytics/dead-letters/{id}
        group.MapGet("/dead-letters/{id:guid}", async (Guid id, ModulusAnalyticsDbContext db, CancellationToken ct) =>
        {
            var row = await db.AnalyticsDeliveries.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && d.DeadLetteredAt != null, ct);
            return row is null ? Results.NotFound() : Results.Ok(ToModel(row));
        })
        .WithSummary("Get one dead-lettered analytics record (metadata and error only)");

        // POST /analytics/dead-letters/replay
        group.MapPost("/dead-letters/replay", async (
            AnalyticsReplayRequest request,
            ModulusAnalyticsDbContext db,
            ICurrentUser currentUser,
            TimeProvider clock,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (request.Ids is not { Length: > 0 })
            {
                return Results.BadRequest("Ids must be non-empty.");
            }

            var ids = request.Ids.Distinct().ToArray();
            if (ids.Length > MaxReplayIds)
            {
                return Results.BadRequest($"At most {MaxReplayIds} ids per request.");
            }

            var due = clock.GetUtcNow().UtcDateTime;
            var replayed = await db.AnalyticsDeliveries
                .Where(d => ids.Contains(d.Id) && d.DeadLetteredAt != null)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(d => d.DeadLetteredAt, (DateTime?)null)
                          .SetProperty(d => d.AttemptCount, 0)
                          .SetProperty(d => d.NextAttemptAt, (DateTime?)due)
                          .SetProperty(d => d.LockedBy, (string?)null)
                          .SetProperty(d => d.LockedUntil, (DateTime?)null),
                    ct);

            // LastError stays until the next attempt replaces it, so the history is not lost.
            loggerFactory.CreateLogger("Modulus.Analytics.Management").LogInformation(
                "Analytics replay by {User}: {Replayed} dead-lettered record(s) requeued, {NotFound} not found.",
                currentUser.UserId?.ToString() ?? "unknown", replayed, ids.Length - replayed);
            return Results.Ok(new AnalyticsReplayResponse(replayed, ids.Length - replayed));
        })
        .WithSummary("Put dead-lettered analytics records back in the queue");

        // DELETE /analytics/dead-letters/purge?beforeDays=30
        group.MapDelete("/dead-letters/purge", async (
            int? beforeDays, ModulusAnalyticsDbContext db, ICurrentUser currentUser, TimeProvider clock, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var days = Math.Max(0, beforeDays ?? 30);
            var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-days);
            var purged = await db.AnalyticsDeliveries.Where(d => d.DeadLetteredAt != null && d.DeadLetteredAt < cutoff).ExecuteDeleteAsync(ct);
            loggerFactory.CreateLogger("Modulus.Analytics.Management").LogInformation(
                "Analytics purge by {User}: {Purged} dead letter(s) older than {Days} day(s) deleted.",
                currentUser.UserId?.ToString() ?? "unknown", purged, days);
            return Results.Ok(new AnalyticsPurgeResponse(purged));
        })
        .WithSummary("Delete old dead-lettered analytics records");

        // POST /analytics/backfills
        group.MapPost("/backfills", async (
            AnalyticsBackfillRequest request,
            AnalyticsBackfillManager manager,
            ICurrentUser currentUser,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Dataset) || request.Version < 1)
            {
                return Results.BadRequest("Dataset and a version of 1 or higher are required.");
            }

            if (request.TenantIds is { Length: > MaxBackfillTenants })
            {
                return Results.BadRequest($"At most {MaxBackfillTenants} tenants per request; omit TenantIds to cover every tenant.");
            }

            var who = currentUser.UserId?.ToString() ?? "unknown";
            try
            {
                var result = await manager.StartAsync(request.Dataset, request.Version, request.TenantIds, request.Restart, who, ct);
                loggerFactory.CreateLogger("Modulus.Analytics.Management").LogInformation(
                    "Analytics backfill of {Dataset}.v{Version} started by {User}: {Created} created, {Restarted} restarted, {Skipped} skipped.",
                    request.Dataset, request.Version, who, result.Created, result.Restarted, result.Skipped);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        })
        .WithSummary("Start analytics backfill runs for a dataset version");

        // GET /analytics/backfills?dataset=...&status=Running&take=100
        group.MapGet("/backfills", async (string? dataset, string? status, int? take, AnalyticsBackfillManager manager, CancellationToken ct) =>
        {
            AnalyticsBackfillStatus? parsed = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<AnalyticsBackfillStatus>(status, ignoreCase: true, out var value) || !Enum.IsDefined(value))
                {
                    return Results.BadRequest("Status must be Pending, Running, Completed or Failed.");
                }

                parsed = value;
            }

            var runs = await manager.ListAsync(dataset, parsed, take ?? 100, ct);
            return Results.Ok(runs.Select(r => new AnalyticsBackfillRunInfo(
                r.Id, r.Dataset, r.SchemaVersion, r.TenantId, r.Status.ToString(), r.RowsQueued, r.AttemptCount,
                r.CreatedAt, r.UpdatedAt, r.CompletedAt, r.NextAttemptAt, r.LastError, r.RequestedBy)).ToList());
        })
        .WithSummary("List analytics backfill runs");

        return group;
    }

    private static AnalyticsDeadLetter ToModel(AnalyticsDelivery d)
        => new(d.Id, d.TenantId, d.EventId, d.Dataset, d.SchemaVersion, d.OccurredAt, d.CreatedAt, d.DeadLetteredAt, d.AttemptCount, d.LastError);
}
