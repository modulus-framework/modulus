using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Modulus.Authorization.Audit;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;

namespace Modulus.Authorization.Management;

public static partial class AuthorizationManagementExtensions
{
    /// <summary>
    /// Maps access requests and break-glass under <paramref name="prefix"/> (default <c>/authorization/access-requests</c>).
    /// Any signed-in user may ask for access (<c>POST</c>, <c>GET mine</c>, <c>DELETE {id}</c>) and a holder of
    /// <see cref="BreakGlassPermission"/> may activate emergency access; approving, refusing and reviewing need
    /// <see cref="ManagePermission"/>. An approver cannot decide their own request and cannot give what they do not hold
    /// (<see cref="GrantAnyPermission"/> lifts that); approved access is a temporary grant that ends by itself.
    /// </summary>
    public static RouteGroupBuilder MapModulusAccessRequests(
        this IEndpointRouteBuilder endpoints, string prefix = "/authorization/access-requests")
    {
        var group = endpoints.MapGroup(prefix).RequireAuthorization();

        group.MapPost("/", async (
            AccessRequestWriteRequest request, ClaimsPrincipal caller, EfAccessRequestStore requests, IPermissionRegistry registry,
            IOptions<AuthorizationManagementOptions> options, TimeProvider clock,
            IAuthorizationAuditWriter auditWriter, ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (GrantGuards.UserIdOf(caller) is not { } requester)
                return Results.Unauthorized();
            var invalid = ValidateAccessAsk(request.Permissions, request.Reason, request.Hours, registry, options.Value, out var permissions);
            if (invalid is not null)
                return invalid;
            if (await requests.CountPendingAsync(requester, ct) >= options.Value.MaxPendingAccessRequests)
                return Results.Problem(detail: "You have too many requests waiting; wait for a decision or cancel one.", statusCode: StatusCodes.Status429TooManyRequests);

            var saved = await requests.CreateAsync(new AccessRequest(
                Guid.Empty, AccessRequestKind.Request, requester, permissions, request.Reason!.Trim(), request.Hours,
                AccessRequestStatus.Pending, clock.GetUtcNow()), ct);
            await auditWriter.WriteAsync(new AuthorizationAdministrativeChangeEvent(
                "AccessRequest", "Requested", currentUser.UserId?.ToString(), $"request:{saved.Id}",
                new Dictionary<string, string> { ["permissions"] = string.Join(",", permissions), ["hours"] = request.Hours.ToString(System.Globalization.CultureInfo.InvariantCulture) }), ct);
            return Results.Created($"access-requests/{saved.Id}", ToResponse(saved));
        });

        group.MapGet("/mine", async (ClaimsPrincipal caller, EfAccessRequestStore requests, CancellationToken ct) =>
            GrantGuards.UserIdOf(caller) is { } me
                ? Results.Ok((await requests.ListForRequesterAsync(me, ct)).Select(ToResponse))
                : Results.Unauthorized());

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal caller, EfAccessRequestStore requests, TimeProvider clock, CancellationToken ct) =>
        {
            if (GrantGuards.UserIdOf(caller) is not { } me || await requests.GetAsync(id, ct) is not { } existing || existing.RequesterId != me)
                return Results.NotFound();

            return await requests.DecideAsync(id, AccessRequestStatus.Pending, AccessRequestStatus.Cancelled, me, clock.GetUtcNow(), null, null, ct)
                ? Results.NoContent()
                : Results.Conflict(new { error = "Not pending", message = "Only a waiting request can be cancelled." });
        });

        group.MapGet("/pending", async (EfAccessRequestStore requests, CancellationToken ct) =>
            Results.Ok((await requests.ListAsync(AccessRequestStatus.Pending, ct: ct)).Select(ToResponse)))
            .RequireAuthorization(ManagePermission);

        group.MapPost("/{id:guid}/approve", async (
            Guid id, AccessRequestDecisionRequest? decision, ClaimsPrincipal caller,
            EfAccessRequestStore requests, EfPermissionGrantStore grants, IAuthorizationService authorization, ISodPolicy sodPolicy,
            [FromServices] IUserRoleDirectory? roleDirectory, IEffectiveAccessService? effectiveAccessService,
            IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers, ICurrentUser currentUser,
            TimeProvider clock, CancellationToken ct) =>
        {
            if (await requests.GetAsync(id, ct) is not { Kind: AccessRequestKind.Request, Status: AccessRequestStatus.Pending } pending)
                return Results.NotFound();

            var target = $"request:{id}";
            if (GrantGuards.UserIdOf(caller) == pending.RequesterId)
                return await RefuseAsync(auditWriter, currentUser, "AccessRequest", target, "self-approval", "You cannot approve your own request.", ct);
            var notHeld = await GrantGuards.NotHeldAsync(caller, authorization, pending.Permissions);
            if (notHeld.Count > 0)
                return await RefuseAsync(auditWriter, currentUser, "AccessRequest", target,
                    "above-ceiling", $"You cannot grant permissions you do not hold: {string.Join(", ", notHeld)}.", ct);

            var proposed = await ProposedPermissionsAsync(
                GrantHolderType.User, pending.RequesterId.ToString(), null, [.. pending.Permissions], grants, roleDirectory, effectiveAccessService, ct);
            if (proposed.Unknown)
                return Results.Problem(detail: "The requester is unknown, so their role memberships cannot be checked.", statusCode: StatusCodes.Status404NotFound);
            var violations = sodPolicy.Evaluate(proposed.Permissions);
            if (violations.Count > 0)
                return Results.Conflict(new
                {
                    error = "SoD violation",
                    message = "Granting this access would create a segregation-of-duties violation.",
                    violations = violations.Select(v => new { constraint = v.Constraint.Name, held = v.HeldPermissions }).ToList(),
                });

            var now = clock.GetUtcNow();
            var ends = now.AddHours(pending.Hours);
            // The decision first: of two approvers at once, only one wins and makes the grants.
            if (!await requests.DecideAsync(id, AccessRequestStatus.Pending, AccessRequestStatus.Approved, GrantGuards.UserIdOf(caller), now, decision?.Note?.Trim(), ends, ct))
                return Results.Conflict(new { error = "Already decided", message = "Someone decided this request first." });

            foreach (var permission in pending.Permissions)
            {
                await grants.AddScopedGrantAsync(
                    new PermissionGrant(GrantHolderType.User, pending.RequesterId.ToString(), permission, PermissionGrantType.Allow,
                        ValidFrom: now, ValidUntil: ends, Reason: $"access request {id}: {pending.Reason}"),
                    GrantGuards.UserIdOf(caller), now, ct);
            }

            await EmitAuditAsync(auditWriter, observers, currentUser, "AccessRequest", "Approved", target,
                new Dictionary<string, string>
                {
                    ["requester"] = pending.RequesterId.ToString(),
                    ["permissions"] = string.Join(",", pending.Permissions),
                    ["validUntil"] = ends.ToString("O"),
                }, ct);
            return Results.NoContent();
        }).RequireAuthorization(ManagePermission);

        group.MapPost("/{id:guid}/deny", async (
            Guid id, AccessRequestDecisionRequest? decision, ClaimsPrincipal caller, EfAccessRequestStore requests,
            IAuthorizationAuditWriter auditWriter, ICurrentUser currentUser, TimeProvider clock, CancellationToken ct) =>
        {
            if (await requests.GetAsync(id, ct) is not { Kind: AccessRequestKind.Request, Status: AccessRequestStatus.Pending })
                return Results.NotFound();
            if (!await requests.DecideAsync(id, AccessRequestStatus.Pending, AccessRequestStatus.Denied, GrantGuards.UserIdOf(caller), clock.GetUtcNow(), decision?.Note?.Trim(), null, ct))
                return Results.Conflict(new { error = "Already decided", message = "Someone decided this request first." });

            await auditWriter.WriteAsync(new AuthorizationAdministrativeChangeEvent(
                "AccessRequest", "Denied", currentUser.UserId?.ToString(), $"request:{id}", new Dictionary<string, string>()), ct);
            return Results.NoContent();
        }).RequireAuthorization(ManagePermission);

        group.MapPost("/break-glass", async (
            BreakGlassWriteRequest request, ClaimsPrincipal caller, EfAccessRequestStore requests, EfPermissionGrantStore grants,
            IPermissionRegistry registry, IOptions<AuthorizationManagementOptions> options, ISodPolicy sodPolicy,
            [FromServices] IUserRoleDirectory? roleDirectory, IEffectiveAccessService? effectiveAccessService,
            IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers, ICurrentUser currentUser,
            [FromServices] ISecurityAuditLog? securityAudit, TimeProvider clock, CancellationToken ct) =>
        {
            if (GrantGuards.UserIdOf(caller) is not { } me)
                return Results.Unauthorized();
            if (request.Profile is null || !options.Value.BreakGlassProfiles.TryGetValue(request.Profile, out var profile))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["profile"] = ["Unknown break-glass profile."] });
            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 10)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Say why (at least 10 characters): it is reviewed afterwards."] });
            var hours = request.Hours ?? profile.MaxHours;
            if (hours < 1 || hours > profile.MaxHours)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["hours"] = [$"Emergency access lasts 1 to {profile.MaxHours} hours."] });
            _ = GrantGuards.Expand(registry, profile.Permissions, out var unknown);
            if (unknown.Count > 0 || profile.Permissions.Any(p => p.EndsWith(":*", StringComparison.Ordinal)))
                return Results.Problem(detail: "The break-glass profile is misconfigured (unknown permission or wildcard).", statusCode: StatusCodes.Status500InternalServerError);

            // Emergency access may not create the toxic combinations separation of duties exists to prevent.
            var proposed = await ProposedPermissionsAsync(
                GrantHolderType.User, me.ToString(), null, [.. profile.Permissions], grants, roleDirectory, effectiveAccessService, ct);
            if (!proposed.Unknown && sodPolicy.Evaluate(proposed.Permissions).Count > 0)
                return Results.Conflict(new { error = "SoD violation", message = "This emergency access would create a segregation-of-duties violation." });

            var now = clock.GetUtcNow();
            var ends = now.AddHours(hours);
            var record = await requests.CreateAsync(new AccessRequest(
                Guid.Empty, AccessRequestKind.BreakGlass, me, [.. profile.Permissions], request.Reason.Trim(), hours,
                AccessRequestStatus.Activated, now, me, now, request.Profile, ends), ct);
            foreach (var permission in profile.Permissions)
            {
                await grants.AddScopedGrantAsync(
                    new PermissionGrant(GrantHolderType.User, me.ToString(), permission, PermissionGrantType.Allow,
                        ValidFrom: now, ValidUntil: ends, Reason: $"break-glass {request.Profile} {record.Id}: {request.Reason.Trim()}"),
                    me, now, ct);
            }

            securityAudit?.Record(new SecurityAuditEvent
            {
                Category = SecurityAuditCategories.Authorization,
                Action = "break-glass.activated",
                Outcome = SecurityAuditOutcomes.Overridden,
                Actor = me.ToString(),
                Target = $"profile:{request.Profile}",
                Details = new Dictionary<string, string?> { ["request"] = record.Id.ToString(), ["validUntil"] = ends.ToString("O") },
            });
            await EmitAuditAsync(auditWriter, observers, currentUser, "AccessRequest", "BreakGlassActivated", $"request:{record.Id}",
                new Dictionary<string, string> { ["profile"] = request.Profile, ["validUntil"] = ends.ToString("O"), ["reason"] = request.Reason.Trim() }, ct);
            return Results.Created($"access-requests/{record.Id}", ToResponse(record));
        }).RequireAuthorization(BreakGlassPermission);

        group.MapGet("/break-glass/unreviewed", async (EfAccessRequestStore requests, CancellationToken ct) =>
            Results.Ok((await requests.ListAsync(AccessRequestStatus.Activated, AccessRequestKind.BreakGlass, unreviewedOnly: true, ct: ct)).Select(ToResponse)))
            .RequireAuthorization(ManagePermission);

        group.MapPost("/{id:guid}/review", async (
            Guid id, AccessRequestDecisionRequest? review, ClaimsPrincipal caller, EfAccessRequestStore requests,
            IAuthorizationAuditWriter auditWriter, ICurrentUser currentUser, TimeProvider clock, CancellationToken ct) =>
        {
            if (await requests.GetAsync(id, ct) is not { Kind: AccessRequestKind.BreakGlass } use)
                return Results.NotFound();
            // The reviewer is someone other than the person who used the emergency access.
            if (GrantGuards.UserIdOf(caller) == use.RequesterId)
                return await RefuseAsync(auditWriter, currentUser, "AccessRequest", $"request:{id}", "self-review", "You cannot review your own emergency access.", ct);
            if (!await requests.ReviewAsync(id, GrantGuards.UserIdOf(caller), clock.GetUtcNow(), review?.Note?.Trim(), ct))
                return Results.Conflict(new { error = "Already reviewed", message = "This use was already reviewed." });

            await auditWriter.WriteAsync(new AuthorizationAdministrativeChangeEvent(
                "AccessRequest", "BreakGlassReviewed", currentUser.UserId?.ToString(), $"request:{id}", new Dictionary<string, string>()), ct);
            return Results.NoContent();
        }).RequireAuthorization(ManagePermission);

        return group;
    }

    private static IResult? ValidateAccessAsk(
        IReadOnlyList<string>? asked, string? reason, int hours, IPermissionRegistry registry, AuthorizationManagementOptions limits,
        out List<string> permissions)
    {
        permissions = [];
        if (asked is null or { Count: 0 } || asked.Any(p => string.IsNullOrWhiteSpace(p) || p.EndsWith(":*", StringComparison.Ordinal)))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["permissions"] = ["Name the registered permissions you need (no wildcards)."] });
        permissions = [.. asked.Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)];
        _ = GrantGuards.Expand(registry, permissions, out var unknown);
        if (unknown.Count > 0)
            return GrantGuards.Unknown(unknown);
        if (string.IsNullOrWhiteSpace(reason))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Say why you need it: the approver reads this."] });
        if (hours < 1 || TimeSpan.FromHours(hours) > limits.MaxTemporaryGrantDuration)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["hours"] = [$"Ask for 1 to {limits.MaxTemporaryGrantDuration.TotalHours:0} hours."],
            });
        return null;
    }

    private static AccessRequestResponse ToResponse(AccessRequest r)
        => new(r.Id, r.Kind.ToString(), r.RequesterId, r.Permissions, r.Reason, r.Hours, r.Status.ToString(), r.CreatedAt,
            r.DecidedBy, r.DecidedAt, r.Note, r.AccessEndsAt, r.ReviewedBy, r.ReviewedAt);
}
