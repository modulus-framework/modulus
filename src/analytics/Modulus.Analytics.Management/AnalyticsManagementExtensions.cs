namespace Modulus.Analytics.Management;

using System.Security.Claims;
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
    /// <summary>The permission that covers every dataset and the cross-dataset views (queue status, purge of all dead letters).</summary>
    public const string ManagePermission = "analytics:manage";

    /// <summary>The permission that covers one dataset: <c>analytics:manage:{dataset}</c>. It is declared for every registered dataset.</summary>
    /// <param name="dataset">The dataset name.</param>
    public static string DatasetPermission(string dataset) => $"{ManagePermission}:{dataset}";

    private const int MaxReplayIds = 1000;
    private const int MaxBackfillTenants = 1000;

    /// <summary>
    /// Declares <see cref="ManagePermission"/> and one <see cref="DatasetPermission"/> per registered dataset (all critical: they need a step-up sign-in in
    /// production and cannot come from a wildcard grant). Needs <c>AddModulusAnalyticsStore&lt;TContext&gt;()</c>, because the endpoints operate on the delivery store.
    /// </summary>
    /// <remarks>Datasets may be registered before or after this call; the permissions are declared when the permission registry is built.</remarks>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddModulusAnalyticsManagement(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddModulusAnalytics();
        services.AddAuthorization();
        services.TryAddScoped<ICurrentUser, NullCurrentUser>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<AnalyticsAccess>();

        // The same catalog instance AddModulusAnalytics fills, read when the registry is built, after every dataset is registered.
        var catalog = (AnalyticsDatasetCatalog)services.First(d => d.ServiceType == typeof(AnalyticsDatasetCatalog)).ImplementationInstance!;
        return services.AddPermissions("Modulus.Analytics", registry =>
        {
            registry.Add(
                ManagePermission,
                "Manage analytics delivery for every dataset: queue status, freshness, dead letters, backfill and incremental runs, reconciliation.",
                null,
                PermissionSensitivity.Critical);
            foreach (var name in catalog.All.Select(d => d.Name).Distinct(StringComparer.Ordinal))
            {
                registry.Add(
                    DatasetPermission(name),
                    $"Manage analytics delivery for the '{name}' dataset only: its freshness, dead letters, backfill and incremental runs, and reconciliation.",
                    null,
                    PermissionSensitivity.Critical);
            }
        });
    }

    /// <summary>
    /// Maps the endpoints under <paramref name="prefix"/>. A caller must be signed in; what they may do is decided per request from
    /// <see cref="ManagePermission"/> and <see cref="DatasetPermission"/>. A caller with neither gets 403, and one limited to some datasets never sees another's.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="prefix">The route prefix. Defaults to <c>/analytics</c>.</param>
    /// <returns>The group, so hosts can attach further conventions.</returns>
    public static RouteGroupBuilder MapModulusAnalyticsManagement(this IEndpointRouteBuilder endpoints, string prefix = "/analytics")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix)
            .RequireAuthorization()
            .WithTags("Analytics Management");

        // GET /analytics/status (cross-dataset: needs the global permission)
        group.MapGet("/status", async (ClaimsPrincipal user, AnalyticsAccess access, ModulusAnalyticsDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!(await access.ScopeAsync(user)).IsGlobal)
            {
                return Forbidden();
            }

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

        // POST /analytics/reconciliations
        group.MapPost("/reconciliations", async (
            AnalyticsReconcileRequest request,
            ClaimsPrincipal user,
            AnalyticsAccess access,
            AnalyticsReconciler reconciler,
            ICurrentUser currentUser,
            ILoggerFactory loggerFactory,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Dataset) || request.Version < 1)
            {
                return Results.BadRequest("Dataset and a version of 1 or higher are required.");
            }

            if (!await access.CanAsync(user, request.Dataset))
            {
                return Forbidden();
            }

            if (request.TenantIds is { Length: > MaxBackfillTenants })
            {
                return Results.BadRequest($"At most {MaxBackfillTenants} tenants per request; omit TenantIds to cover every tenant.");
            }

            try
            {
                var report = await reconciler.ReconcileAsync(request.Dataset, request.Version, request.TenantIds, ct);
                loggerFactory.CreateLogger("Modulus.Analytics.Management").LogInformation(
                    "Analytics reconciliation of {Dataset}.v{Version} by {User}: {Results} comparison(s), {Errors} error(s), clean={Clean}.",
                    request.Dataset, request.Version, currentUser.UserId?.ToString() ?? "unknown", report.Results.Count, report.Errors.Count, report.IsClean);
                AnalyticsAudit.Record(services, "reconciliation.run", currentUser.UserId?.ToString(), $"dataset:{request.Dataset}.v{request.Version}",
                    ("tenants", request.TenantIds is null ? "all" : request.TenantIds.Length), ("comparisons", report.Results.Count),
                    ("errors", report.Errors.Count), ("clean", report.IsClean));
                return Results.Ok(report);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        })
        .WithSummary("Compare the warehouse with the operational store, per tenant");

        // GET /analytics/freshness (a dataset-scoped caller sees only their datasets)
        group.MapGet("/freshness", async (ClaimsPrincipal user, AnalyticsAccess access, IAnalyticsFreshnessReader reader, CancellationToken ct) =>
        {
            var scope = await access.ScopeAsync(user);
            if (!scope.Any)
            {
                return Forbidden();
            }

            return Results.Ok((await reader.ReadAsync(ct)).Where(f => scope.Allows(f.Dataset)).ToList());
        })
        .WithSummary("Show each dataset's lag against its freshness SLA");

        // GET /analytics/dead-letters?page=1&pageSize=20&dataset=...&tenant=...
        group.MapGet("/dead-letters", async (
            int? page, int? pageSize, string? dataset, string? tenant, ClaimsPrincipal user, AnalyticsAccess access, ModulusAnalyticsDbContext db, CancellationToken ct) =>
        {
            var scope = await access.ScopeAsync(user);
            if (!scope.Any)
            {
                return Forbidden();
            }

            var p = Math.Max(1, page ?? 1);
            var ps = pageSize is >= 1 and <= 1000 ? pageSize.Value : 20;
            var query = db.AnalyticsDeliveries.AsNoTracking().Where(d => d.DeadLetteredAt != null);
            if (!scope.IsGlobal)
            {
                var allowed = scope.Datasets.ToArray();
                query = query.Where(d => allowed.Contains(d.Dataset));
            }

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
        group.MapGet("/dead-letters/{id:guid}", async (Guid id, ClaimsPrincipal user, AnalyticsAccess access, ModulusAnalyticsDbContext db, CancellationToken ct) =>
        {
            var scope = await access.ScopeAsync(user);
            if (!scope.Any)
            {
                return Forbidden();
            }

            var row = await db.AnalyticsDeliveries.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && d.DeadLetteredAt != null, ct);

            // A dataset the caller may not manage looks exactly like a record that does not exist.
            return row is null || !scope.Allows(row.Dataset) ? Results.NotFound() : Results.Ok(ToModel(row));
        })
        .WithSummary("Get one dead-lettered analytics record (metadata and error only)");

        // POST /analytics/dead-letters/replay
        // POST /analytics/quality: profiles the warehouse rows per company and reports the data-quality rules they break (counts only, never values).
        group.MapPost("/quality", async (
            AnalyticsQualityRequest request,
            ClaimsPrincipal user,
            AnalyticsAccess access,
            AnalyticsQualityChecker checker,
            ICurrentUser currentUser,
            ILoggerFactory loggerFactory,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Dataset) || request.Version < 1)
            {
                return Results.BadRequest("Dataset and a version of 1 or higher are required.");
            }

            if (!await access.CanAsync(user, request.Dataset))
            {
                return Forbidden();
            }

            if (request.TenantIds is { Length: > MaxBackfillTenants })
            {
                return Results.BadRequest($"At most {MaxBackfillTenants} tenants per request; omit TenantIds to cover every tenant.");
            }

            try
            {
                var report = await checker.CheckAsync(request.Dataset, request.Version, request.TenantIds, ct);
                var findings = report.Results.Sum(r => r.Findings.Count);
                loggerFactory.CreateLogger("Modulus.Analytics.Management").LogInformation(
                    "Analytics quality check of {Dataset}.v{Version} by {User}: {Results} result(s), {Findings} finding(s), {Errors} error(s).",
                    request.Dataset, request.Version, currentUser.UserId?.ToString() ?? "unknown", report.Results.Count, findings, report.Errors.Count);
                AnalyticsAudit.Record(services, "quality.check", currentUser.UserId?.ToString(), $"dataset:{request.Dataset}.v{request.Version}",
                    ("tenants", request.TenantIds is null ? "all" : request.TenantIds.Length), ("results", report.Results.Count),
                    ("findings", findings), ("errors", report.Errors.Count));
                return Results.Ok(report);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        })
        .WithSummary("Check the warehouse's data quality for a dataset version, per company");

        group.MapPost("/dead-letters/replay", async (
            AnalyticsReplayRequest request,
            ClaimsPrincipal user,
            AnalyticsAccess access,
            ModulusAnalyticsDbContext db,
            ICurrentUser currentUser,
            TimeProvider clock,
            ILoggerFactory loggerFactory,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            var scope = await access.ScopeAsync(user);
            if (!scope.Any)
            {
                return Forbidden();
            }

            if (request.Ids is not { Length: > 0 })
            {
                return Results.BadRequest("Ids must be non-empty.");
            }

            var ids = request.Ids.Distinct().ToArray();
            if (ids.Length > MaxReplayIds)
            {
                return Results.BadRequest($"At most {MaxReplayIds} ids per request.");
            }

            // Only the records of datasets the caller may manage are replayed; the rest count as not found, so an id of another dataset reveals nothing.
            var candidates = db.AnalyticsDeliveries.Where(d => ids.Contains(d.Id) && d.DeadLetteredAt != null);
            if (!scope.IsGlobal)
            {
                var allowed = scope.Datasets.ToArray();
                candidates = candidates.Where(d => allowed.Contains(d.Dataset));
            }

            var due = clock.GetUtcNow().UtcDateTime;
            var replayed = await candidates.ExecuteUpdateAsync(
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
            AnalyticsAudit.Record(services, "dead-letters.replay", currentUser.UserId?.ToString(), "dead-letters",
                ("requested", ids.Length), ("replayed", replayed));
            return Results.Ok(new AnalyticsReplayResponse(replayed, ids.Length - replayed));
        })
        .WithSummary("Put dead-lettered analytics records back in the queue");

        // DELETE /analytics/dead-letters/purge?beforeDays=30&dataset=orders (without a dataset, it purges every dataset and needs the global permission)
        group.MapDelete("/dead-letters/purge", async (
            int? beforeDays,
            string? dataset,
            ClaimsPrincipal user,
            AnalyticsAccess access,
            ModulusAnalyticsDbContext db,
            ICurrentUser currentUser,
            TimeProvider clock,
            ILoggerFactory loggerFactory,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            var scope = await access.ScopeAsync(user);
            if (!scope.Any || (string.IsNullOrWhiteSpace(dataset) && !scope.IsGlobal) || (!string.IsNullOrWhiteSpace(dataset) && !scope.Allows(dataset)))
            {
                return Forbidden();
            }

            var days = Math.Max(0, beforeDays ?? 30);
            var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-days);
            var doomed = db.AnalyticsDeliveries.Where(d => d.DeadLetteredAt != null && d.DeadLetteredAt < cutoff);
            if (!string.IsNullOrWhiteSpace(dataset))
            {
                doomed = doomed.Where(d => d.Dataset == dataset);
            }

            var purged = await doomed.ExecuteDeleteAsync(ct);
            loggerFactory.CreateLogger("Modulus.Analytics.Management").LogInformation(
                "Analytics purge by {User}: {Purged} dead letter(s) older than {Days} day(s) deleted ({Dataset}).",
                currentUser.UserId?.ToString() ?? "unknown", purged, days, string.IsNullOrWhiteSpace(dataset) ? "all datasets" : dataset);
            AnalyticsAudit.Record(services, "dead-letters.purge", currentUser.UserId?.ToString(),
                string.IsNullOrWhiteSpace(dataset) ? "dead-letters" : $"dataset:{dataset}", ("olderThanDays", days), ("purged", purged));
            return Results.Ok(new AnalyticsPurgeResponse(purged));
        })
        .WithSummary("Delete old dead-lettered analytics records");

        // POST /analytics/backfills
        group.MapPost("/backfills", async (
            AnalyticsBackfillRequest request,
            ClaimsPrincipal user,
            AnalyticsAccess access,
            AnalyticsBackfillManager manager,
            ICurrentUser currentUser,
            ILoggerFactory loggerFactory,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Dataset) || request.Version < 1)
            {
                return Results.BadRequest("Dataset and a version of 1 or higher are required.");
            }

            if (!await access.CanAsync(user, request.Dataset))
            {
                return Forbidden();
            }

            if (request.TenantIds is { Length: > MaxBackfillTenants })
            {
                return Results.BadRequest($"At most {MaxBackfillTenants} tenants per request; omit TenantIds to cover every tenant.");
            }

            var who = currentUser.UserId?.ToString() ?? "unknown";
            try
            {
                var result = await manager.StartAsync(
                    request.Dataset, request.Version, request.TenantIds, request.Restart, who, ct, request.Incremental ? AnalyticsRunKind.Incremental : AnalyticsRunKind.Backfill);
                loggerFactory.CreateLogger("Modulus.Analytics.Management").LogInformation(
                    "Analytics {Kind} of {Dataset}.v{Version} started by {User}: {Created} created, {Restarted} restarted, {Skipped} skipped, {NotEntitled} not entitled.",
                    request.Incremental ? "incremental sync" : "backfill", request.Dataset, request.Version, who, result.Created, result.Restarted, result.Skipped, result.NotEntitled);
                AnalyticsAudit.Record(services, request.Incremental ? "incremental.start" : "backfill.start", who, $"dataset:{request.Dataset}.v{request.Version}",
                    ("tenants", request.TenantIds is null ? "all" : request.TenantIds.Length), ("restart", request.Restart), ("created", result.Created),
                    ("restarted", result.Restarted), ("skipped", result.Skipped), ("notEntitled", result.NotEntitled));
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        })
        .WithSummary("Start analytics backfill or incremental runs for a dataset version");

        // GET /analytics/backfills?dataset=...&status=Running&take=100 (a dataset-scoped caller sees only their datasets)
        group.MapGet("/backfills", async (
            string? dataset, string? status, int? take, ClaimsPrincipal user, AnalyticsAccess access, AnalyticsBackfillManager manager, CancellationToken ct) =>
        {
            var scope = await access.ScopeAsync(user);
            if (!scope.Any)
            {
                return Forbidden();
            }

            AnalyticsBackfillStatus? parsed = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<AnalyticsBackfillStatus>(status, ignoreCase: true, out var value) || !Enum.IsDefined(value))
                {
                    return Results.BadRequest("Status must be Pending, Running, Completed, Failed or Cancelled.");
                }

                parsed = value;
            }

            var runs = await manager.ListAsync(dataset, parsed, take ?? 100, ct, scope.IsGlobal ? null : scope.Datasets);
            return Results.Ok(runs.Select(r => new AnalyticsBackfillRunInfo(
                r.Id, r.Dataset, r.SchemaVersion, r.TenantId, r.Kind.ToString(), r.Status.ToString(), r.RowsQueued, r.AttemptCount,
                r.CreatedAt, r.UpdatedAt, r.CompletedAt, r.NextAttemptAt, r.LastError, r.RequestedBy)).ToList());
        })
        .WithSummary("List analytics backfill and incremental runs");

        // POST /analytics/backfills/{id}/cancel
        group.MapPost("/backfills/{id:guid}/cancel", async (
            Guid id, ClaimsPrincipal user, AnalyticsAccess access, AnalyticsBackfillManager manager, ICurrentUser currentUser, ILoggerFactory loggerFactory,
            IServiceProvider services, CancellationToken ct) =>
        {
            var scope = await access.ScopeAsync(user);
            if (!scope.Any)
            {
                return Forbidden();
            }

            var run = await manager.GetAsync(id, ct);
            if (run is null || !scope.Allows(run.Dataset) || !await manager.CancelAsync(id, ct))
            {
                return Results.NotFound("No waiting or running run has that id.");
            }

            loggerFactory.CreateLogger("Modulus.Analytics.Management").LogInformation(
                "Analytics run {Run} cancelled by {User}.", id, currentUser.UserId?.ToString() ?? "unknown");
            AnalyticsAudit.Record(services, "run.cancel", currentUser.UserId?.ToString(), $"run:{id}", ("dataset", run.Dataset));
            return Results.Ok();
        })
        .WithSummary("Stop a waiting or running backfill or incremental run");

        return group;
    }

    private static IResult Forbidden() => Results.StatusCode(StatusCodes.Status403Forbidden);

    private static AnalyticsDeadLetter ToModel(AnalyticsDelivery d)
        => new(d.Id, d.TenantId, d.EventId, d.Dataset, d.SchemaVersion, d.OccurredAt, d.CreatedAt, d.DeadLetteredAt, d.AttemptCount, d.LastError);
}
