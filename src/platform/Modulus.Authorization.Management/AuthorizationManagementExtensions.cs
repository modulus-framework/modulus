using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Modulus.Authorization.Approval;
using Modulus.Authorization.Audit;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Organization;
using Modulus.Authorization.Resources;
using Modulus.Authorization.Scopes;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;

namespace Modulus.Authorization.Management;

/// <summary>
/// Administrative HTTP API over the EF Core-backed authorization stores: grant,
/// org-structure, entitlement, and delegation management as REST endpoints, so
/// operators change authorization data at runtime instead of redeploying seeds.
/// Every endpoint requires the <see cref="ManagePermission"/> permission via the
/// framework's <c>:</c>-policy convention.
/// </summary>
public static class AuthorizationManagementExtensions
{
    /// <summary>The permission guarding every management endpoint.</summary>
    public const string ManagePermission = "authorization:manage";

    /// <summary>
    /// Lets the holder grant permissions they do not hold themselves (BR-012). Without it an administrator can grant
    /// only what they hold, so security administration never doubles as business authority.
    /// </summary>
    public const string GrantAnyPermission = "authorization:grant-any";

    /// <summary>
    /// Guards the platform-wide entitlement endpoints (plans, per-company feature overrides). Held by host operators
    /// only: a company administrator holds <see cref="ManagePermission"/> but must never reach other companies' plans.
    /// </summary>
    public const string EntitlementsPermission = "authorization:entitlements:manage";

    /// <summary>
    /// Declares the <see cref="ManagePermission"/> permission in the registry.
    /// Requires <c>AddModulusAuthorization()</c> and
    /// <c>AddEfCoreAuthorizationStores(...)</c> — the endpoints operate on the
    /// concrete EF stores.
    /// </summary>
    public static IServiceCollection AddModulusAuthorizationManagement(
        this IServiceCollection services)
    {
        // The endpoint policies run through UseAuthorization, which needs the
        // full AddAuthorization registration (AddModulusAuthorization only adds
        // AddAuthorizationCore). Idempotent if the host already called it.
        services.AddAuthorization();
        services.AddOptions<AuthorizationManagementOptions>();

        // Every mutating endpoint resolves ICurrentUser to attribute the audit
        // event (blueprint §5.14/§16). Normally registered by AddModulus/
        // AddMediator/AddModulusIdentity — TryAdd so this package doesn't
        // require any of those specifically, matching the framework's own
        // fail-safe-default convention for this seam.
        services.TryAddScoped<ICurrentUser, NullCurrentUser>();

        return services.AddPermissions("Modulus.Authorization", registry =>
        {
            registry.Add(
                ManagePermission,
                "Manage authorization data: grants, org structure and delegations of the current company.",
                null,
                PermissionSensitivity.Critical);
            registry.Add(
                GrantAnyPermission,
                "Grant permissions the administrator does not hold themselves.",
                null,
                PermissionSensitivity.Critical);
            registry.Add(
                EntitlementsPermission,
                "Manage platform-wide feature plans and per-company overrides (host operators only).",
                null,
                PermissionSensitivity.Critical);
        });
    }

    /// <summary>
    /// Maps the management endpoints under <paramref name="prefix"/>, all guarded
    /// by <see cref="ManagePermission"/>. Returns the group so hosts can attach
    /// further conventions (rate limits, OpenAPI tags, …).
    /// </summary>
    public static RouteGroupBuilder MapModulusAuthorizationManagement(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/authorization")
    {
        var group = endpoints.MapGroup(prefix).RequireAuthorization(ManagePermission);

        MapGrants(group);
        MapScopedGrants(group);
        MapApprovalAuthorities(group);
        MapOrganization(group);
        MapEntitlements(group);
        MapDelegations(group);
        MapGovernance(group);
        return group;
    }

    /// <summary>
    /// Maps per-record authorization questions under <paramref name="prefix"/> (default <c>/authorization/resources</c>):
    /// <c>GET {type}/{id}/actions</c> (any signed-in caller: what may I do to this record now?) and
    /// <c>GET {type}/{id}/explain?action=</c> (<see cref="ManagePermission"/>: which policy rules matched, evaluated as the
    /// caller). Types come from <c>AddResourceLocator</c>; an unknown type or id is a <c>404</c> either way.
    /// </summary>
    public static RouteGroupBuilder MapModulusResourceAuthorization(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/authorization/resources")
    {
        var group = endpoints.MapGroup(prefix).RequireAuthorization();

        group.MapGet("/{resourceType}/{id}/actions", async (
            string resourceType, string id,
            IResourceLocator locator, IResourceAuthorizer authorizer, IResourcePolicyRegistry registry,
            CancellationToken ct) =>
        {
            var record = await locator.FindAsync(resourceType, id, ct);
            if (record is null)
                return Results.NotFound();

            return Results.Ok(new { actions = await authorizer.GetAvailableActionsAsync(registry, record, ct) });
        });

        group.MapGet("/{resourceType}/{id}/explain", async (
            string resourceType, string id, string action,
            IResourceLocator locator, IServiceProvider services, CancellationToken ct) =>
        {
            var record = await locator.FindAsync(resourceType, id, ct);
            if (record is null)
                return Results.NotFound();

            var explanation = ActivatorUtilities.CreateInstance<ResourceAuthorizer>(services).Explain(record, action);
            return Results.Ok(new
            {
                allowed = explanation.Decision.IsAllowed,
                code = explanation.Decision.Code,
                reason = explanation.Decision.Reason,
                rules = explanation.Rules.Select(r => new
                {
                    r.Index,
                    effect = r.Effect.ToString(),
                    r.Action,
                    r.Matched,
                    r.Faulted,
                }),
            });
        }).RequireAuthorization(ManagePermission);

        return group;
    }

    // ── Grants ─────────────────────────────────────────────────────

    private static void MapGrants(RouteGroupBuilder group)
    {
        group.MapGet("/grants/{holderType}/{holder}", async (
            string holderType, string holder,
            EfPermissionGrantStore store, CancellationToken ct) =>
        {
            if (!Enum.TryParse<GrantHolderType>(holderType, ignoreCase: true, out var type))
                return InvalidEnum("holderType", holderType, typeof(GrantHolderType));

            var grants = await store.GetGrantsForHolderAsync(type, holder, ct);
            return Results.Ok(grants.Select(g => new GrantResponse(
                g.HolderType.ToString(), g.Holder, g.Permission, g.Type.ToString())));
        });

        group.MapPost("/grants", async (
            GrantWriteRequest request, ClaimsPrincipal caller,
            EfPermissionGrantStore store, IPermissionRegistry registry, IAuthorizationService authorization,
            IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, ISodPolicy sodPolicy, PermissionResolver resolver,
            [FromServices] IUserRoleDirectory? roleDirectory, IEffectiveAccessService? effectiveAccessService, CancellationToken ct) =>
        {
            if (!Enum.TryParse<GrantHolderType>(request.HolderType, ignoreCase: true, out var holderType))
                return InvalidEnum("holderType", request.HolderType, typeof(GrantHolderType));
            var typeToken = request.Type ?? nameof(PermissionGrantType.Allow);
            if (!Enum.TryParse<PermissionGrantType>(typeToken, ignoreCase: true, out var grantType))
                return InvalidEnum("type", typeToken, typeof(PermissionGrantType));
            if (request.Permissions is not { Length: > 0 })
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["permissions"] = ["At least one permission is required."],
                });

            // Only permissions modules declared in code can be granted or denied (C-2): administrators grant, never invent.
            var expanded = GrantGuards.Expand(registry, request.Permissions, out var unknown);
            if (unknown.Count > 0)
                return GrantGuards.Unknown(unknown);

            var allow = grantType is PermissionGrantType.Allow;
            var target = $"{holderType}:{request.Holder}";
            if (allow)
            {
                // BR-011: nobody widens their own access, not even through a role they already hold.
                if (GrantGuards.TargetsCaller(caller, holderType, request.Holder))
                    return await RefuseAsync(auditWriter, currentUser, "Grant", target,
                        "self-grant", "You cannot grant permissions to yourself or to a role you hold.", ct);

                // BR-012: an administrator grants only what they hold, unless they carry the grant-any permission.
                var notHeld = await GrantGuards.NotHeldAsync(caller, authorization, expanded);
                if (notHeld.Count > 0)
                    return await RefuseAsync(auditWriter, currentUser, "Grant", target,
                        "above-ceiling", $"You cannot grant permissions you do not hold: {string.Join(", ", notHeld)}.", ct);

                // FR-WFL-004: a grant must not complete a toxic combination. Roles come from the identity store, never the request.
                var proposed = await ProposedPermissionsAsync(
                    holderType, request.Holder, request.HolderRoles, expanded, store, roleDirectory, effectiveAccessService, ct);
                if (proposed.Unknown)
                    return Results.Problem(detail: "The user is unknown, so their role memberships cannot be checked.",
                        statusCode: StatusCodes.Status404NotFound);

                var violations = sodPolicy.Evaluate(proposed.Permissions);
                if (violations.Count > 0)
                {
                    return Results.Conflict(new
                    {
                        error = "SoD violation",
                        message = "Granting these permissions would create segregation-of-duties violations.",
                        violations = violations
                            .Select(v => new { constraint = v.Constraint.Name, held = v.HeldPermissions })
                            .ToList(),
                    });
                }
            }

            if (holderType is GrantHolderType.Role)
                await (allow
                    ? store.GrantToRoleAsync(request.Holder, request.Permissions, ct)
                    : store.DenyToRoleAsync(request.Holder, request.Permissions, ct));
            else
                await (allow
                    ? store.GrantToUserAsync(ParseUser(request.Holder), request.Permissions, ct)
                    : store.DenyToUserAsync(ParseUser(request.Holder), request.Permissions, ct));

            await EmitAuditAsync(auditWriter, observers, currentUser, "Grant", allow ? "Granted" : "Denied",
                target,
                new Dictionary<string, string> { ["permissions"] = string.Join(",", request.Permissions) }, ct);

            return Results.NoContent();
        });

        group.MapDelete("/grants/{holderType}/{holder}/{permission}", async (
            string holderType, string holder, string permission, ClaimsPrincipal caller,
            EfPermissionGrantStore store, IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!Enum.TryParse<GrantHolderType>(holderType, ignoreCase: true, out var type))
                return InvalidEnum("holderType", holderType, typeof(GrantHolderType));

            // BR-011: removing a deny that applies to yourself widens your own access just like a grant does.
            if (GrantGuards.TargetsCaller(caller, type, holder))
            {
                var own = await store.GetGrantsForHolderAsync(type, holder, ct);
                if (own.Any(g => g.Type is PermissionGrantType.Deny
                        && string.Equals(g.Permission, permission, StringComparison.OrdinalIgnoreCase)))
                    return await RefuseAsync(auditWriter, currentUser, "Grant", $"{type}:{holder}",
                        "self-grant", "You cannot lift a denial that applies to yourself.", ct);
            }

            if (type is GrantHolderType.Role)
                await store.RevokeFromRoleAsync(holder, permission, ct);
            else
                await store.RevokeFromUserAsync(ParseUser(holder), permission, ct);

            await EmitAuditAsync(auditWriter, observers, currentUser, "Grant", "Revoked",
                $"{holderType}:{holder}",
                new Dictionary<string, string> { ["permission"] = permission }, ct);

            return Results.NoContent();
        });
    }

    /// <summary>The permissions the holder would effectively have after the grant, for the SoD simulation.</summary>
    private static async Task<(bool Unknown, HashSet<string> Permissions)> ProposedPermissionsAsync(
        GrantHolderType holderType, string holder, string[]? holderRoles, List<string> granted,
        EfPermissionGrantStore store, [FromServices] IUserRoleDirectory? roleDirectory,
        IEffectiveAccessService? effectiveAccessService, CancellationToken ct)
    {
        var proposed = new HashSet<string>(granted, StringComparer.OrdinalIgnoreCase);

        if (holderType is GrantHolderType.Role)
        {
            // A role's own grants must stay free of toxic pairs, whoever ends up holding the role.
            foreach (var existing in await store.GetGrantsForHolderAsync(holderType, holder, ct))
            {
                if (existing.Type is PermissionGrantType.Allow)
                    proposed.Add(existing.Permission);
            }

            // Scoped and temporary grants count too: a toxic pair split across scopes is still a toxic pair.
            foreach (var existing in await store.GetScopedGrantsForHolderAsync(holderType, holder, ct))
            {
                if (existing.Grant.Type is PermissionGrantType.Allow)
                    proposed.Add(existing.Grant.Permission);
            }

            return (false, proposed);
        }

        if (effectiveAccessService is null)
            return (false, proposed);

        var userId = ParseUser(holder);
        IReadOnlyCollection<string> roles;
        if (roleDirectory is not null)
        {
            if (await roleDirectory.GetRolesAsync(userId, ct) is not { } known)
                return (true, proposed);
            roles = known;
        }
        else
        {
            // Without an identity store the caller's list is all there is; it can only make the check stricter or looser,
            // so register an IUserRoleDirectory (AddModulusIdentity does) for an authoritative answer.
            roles = holderRoles ?? [];
        }

        var report = effectiveAccessService.Report(new PrincipalGrantQuery(userId, roles));
        proposed.UnionWith(report.AllPermissions);
        return (false, proposed);
    }

    private static async Task<IResult> RefuseAsync(
        IAuthorizationAuditWriter auditWriter, ICurrentUser currentUser,
        string category, string target, string reason, string detail, CancellationToken ct)
    {
        // A refused administrative change is evidence too (FR-AUD-003): record who tried what.
        await auditWriter.WriteAsync(
            new AuthorizationAdministrativeChangeEvent(
                category, "Refused", currentUser.UserId?.ToString(), target,
                new Dictionary<string, string> { ["reason"] = reason }),
            ct);
        return GrantGuards.Refused(detail);
    }


    // ── Scoped, temporary and restricting grants; assignments ───────

    private static void MapScopedGrants(RouteGroupBuilder group)
    {
        group.MapGet("/scoped-grants/{holderType}/{holder}", async (
            string holderType, string holder, EfPermissionGrantStore store, CancellationToken ct) =>
        {
            if (!Enum.TryParse<GrantHolderType>(holderType, ignoreCase: true, out var type))
                return InvalidEnum("holderType", holderType, typeof(GrantHolderType));

            return Results.Ok((await store.GetScopedGrantsForHolderAsync(type, holder, ct)).Select(ToResponse));
        });

        group.MapPost("/scoped-grants", async (
            ScopedGrantWriteRequest request, ClaimsPrincipal caller,
            EfPermissionGrantStore store, IPermissionRegistry registry, IAuthorizationService authorization,
            IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, ISodPolicy sodPolicy, IOrgHierarchy hierarchy, IScopeMapRegistry scopeMaps,
            [FromServices] IUserRoleDirectory? roleDirectory, IEffectiveAccessService? effectiveAccessService,
            IOptions<AuthorizationManagementOptions> limits, TimeProvider clock, CancellationToken ct) =>
        {
            if (!Enum.TryParse<GrantHolderType>(request.HolderType, ignoreCase: true, out var holderType))
                return InvalidEnum("holderType", request.HolderType, typeof(GrantHolderType));
            var typeToken = request.Type ?? nameof(PermissionGrantType.Allow);
            if (!Enum.TryParse<PermissionGrantType>(typeToken, ignoreCase: true, out var grantType))
                return InvalidEnum("type", typeToken, typeof(PermissionGrantType));

            var scope = PermissionScope.Tenant;
            if (request.Scope is not null && !PermissionScope.TryParse(request.Scope, out scope))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["scope"] = ["Use tenant, own, org, org:{unitId} or assigned:{type}."],
                });
            if (scope.OrgUnitId is { } unit && !hierarchy.Contains(unit))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["scope"] = [$"Unknown org unit {unit}."],
                });
            if (scope.Kind is ScopeKind.Assigned && UnknownAssignmentType(scopeMaps, scope.Value) is { } unknownType)
                return unknownType;

            if (request.Permission is null || request.Permission.EndsWith(":*", StringComparison.Ordinal))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["permission"] = ["Name one registered permission; wildcards belong in role definitions, not in scoped or temporary grants."],
                });
            _ = GrantGuards.Expand(registry, [request.Permission], out var unknown);
            if (unknown.Count > 0)
                return GrantGuards.Unknown(unknown);

            var now = clock.GetUtcNow();
            if (request.ValidFrom is { } from && request.ValidUntil is { } until && until <= from)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["validUntil"] = ["A grant must end after it begins."] });
            if (request.ValidUntil is { } ends)
            {
                if (ends <= now)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["validUntil"] = ["A temporary grant must end in the future."] });
                if (ends - (request.ValidFrom ?? now) > limits.Value.MaxTemporaryGrantDuration)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["validUntil"] = [$"A temporary grant may last at most {limits.Value.MaxTemporaryGrantDuration.TotalDays:0.#} days."],
                    });
                if (string.IsNullOrWhiteSpace(request.Reason))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["A temporary grant needs a reason (it is kept for review)."] });
            }

            if (grantType is PermissionGrantType.Deny && scope.Kind is not ScopeKind.Tenant)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["scope"] = ["A deny removes the permission and takes no scope; use type Restrict to narrow it."],
                });
            if (grantType is PermissionGrantType.Restrict && scope.Kind is ScopeKind.Tenant)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["scope"] = ["A restriction needs a scope narrower than the whole company."],
                });

            var target = $"{holderType}:{request.Holder}";
            if (grantType is PermissionGrantType.Allow)
            {
                if (GrantGuards.TargetsCaller(caller, holderType, request.Holder))
                    return await RefuseAsync(auditWriter, currentUser, "Grant", target,
                        "self-grant", "You cannot grant permissions to yourself or to a role you hold.", ct);

                var notHeld = await GrantGuards.NotHeldAsync(caller, authorization, [request.Permission]);
                if (notHeld.Count > 0)
                    return await RefuseAsync(auditWriter, currentUser, "Grant", target,
                        "above-ceiling", $"You cannot grant permissions you do not hold: {string.Join(", ", notHeld)}.", ct);

                var proposed = await ProposedPermissionsAsync(
                    holderType, request.Holder, null, [request.Permission], store, roleDirectory, effectiveAccessService, ct);
                if (proposed.Unknown)
                    return Results.Problem(detail: "The user is unknown, so their role memberships cannot be checked.",
                        statusCode: StatusCodes.Status404NotFound);
                var violations = sodPolicy.Evaluate(proposed.Permissions);
                if (violations.Count > 0)
                {
                    return Results.Conflict(new
                    {
                        error = "SoD violation",
                        message = "Granting this permission would create a segregation-of-duties violation.",
                        violations = violations.Select(v => new { constraint = v.Constraint.Name, held = v.HeldPermissions }).ToList(),
                    });
                }
            }

            var saved = await store.AddScopedGrantAsync(
                new PermissionGrant(holderType, request.Holder, request.Permission, grantType, scope,
                    request.ValidFrom, request.ValidUntil, request.Reason?.Trim()),
                GrantGuards.UserIdOf(caller), now, ct);

            await EmitAuditAsync(auditWriter, observers, currentUser, "Grant", $"Scoped{grantType}", target,
                new Dictionary<string, string>
                {
                    ["permission"] = request.Permission,
                    ["scope"] = scope.Format(),
                    ["validFrom"] = request.ValidFrom?.ToString("O") ?? "",
                    ["validUntil"] = request.ValidUntil?.ToString("O") ?? "",
                    ["reason"] = request.Reason ?? "",
                }, ct);

            return Results.Created($"scoped-grants/{holderType}/{request.Holder}", ToResponse(saved));
        });

        group.MapDelete("/scoped-grants/{id:guid}", async (
            Guid id, ClaimsPrincipal caller, EfPermissionGrantStore store,
            IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (await store.GetScopedGrantAsync(id, ct) is not { } existing)
                return Results.Problem(detail: "Scoped grant not found.", statusCode: StatusCodes.Status404NotFound);

            var grant = existing.Grant;
            // BR-011: lifting a restriction or deny that applies to yourself widens your own access.
            if (grant.Type is not PermissionGrantType.Allow && GrantGuards.TargetsCaller(caller, grant.HolderType, grant.Holder))
                return await RefuseAsync(auditWriter, currentUser, "Grant", $"{grant.HolderType}:{grant.Holder}",
                    "self-grant", "You cannot lift a restriction or denial that applies to yourself.", ct);

            await store.RemoveScopedGrantAsync(id, ct);
            await EmitAuditAsync(auditWriter, observers, currentUser, "Grant", "ScopedRevoked",
                $"{grant.HolderType}:{grant.Holder}",
                new Dictionary<string, string> { ["permission"] = grant.Permission, ["scope"] = grant.EffectiveScope.Format() }, ct);
            return Results.NoContent();
        });

        // The vocabulary of "Assigned" scopes: the access keys the scope maps declare.
        group.MapGet("/assignment-types", (IScopeMapRegistry scopeMaps) => Results.Ok(scopeMaps.AssignmentTypes.Order(StringComparer.OrdinalIgnoreCase)));

        group.MapGet("/assignments/{userId:guid}", async (Guid userId, EfAssignmentStore store, CancellationToken ct) =>
            Results.Ok(await store.ListAsync(userId, ct)));

        group.MapPost("/assignments", async (
            AssignmentWriteRequest request, EfAssignmentStore store, IScopeMapRegistry scopeMaps,
            [FromServices] IUserRoleDirectory? roleDirectory,
            IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.AssignmentType))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["assignmentType"] = ["An assignment type is required."] });
            if (UnknownAssignmentType(scopeMaps, request.AssignmentType) is { } unknownType)
                return unknownType;
            if (request is { ValidFrom: { } from, ValidUntil: { } until } && until <= from)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["validUntil"] = ["An assignment must end after it begins."] });
            if (roleDirectory is not null && await roleDirectory.GetRolesAsync(request.UserId, ct) is null)
                return Results.Problem(detail: "The user is unknown.", statusCode: StatusCodes.Status404NotFound);

            await store.AssignAsync(new Assignment(request.UserId, request.AssignmentType, request.TargetId, request.ValidFrom, request.ValidUntil), ct);
            await EmitAuditAsync(auditWriter, observers, currentUser, "Assignment", "Assigned",
                $"user:{request.UserId} -> {request.AssignmentType.Trim().ToLowerInvariant()}:{request.TargetId}",
                new Dictionary<string, string>
                {
                    ["validFrom"] = request.ValidFrom?.ToString("O") ?? "",
                    ["validUntil"] = request.ValidUntil?.ToString("O") ?? "",
                }, ct);
            return Results.NoContent();
        });

        group.MapDelete("/assignments/{userId:guid}/{assignmentType}/{targetId:guid}", async (
            Guid userId, string assignmentType, Guid targetId, EfAssignmentStore store,
            IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!await store.UnassignAsync(userId, assignmentType, targetId, ct))
                return Results.Problem(detail: "Assignment not found.", statusCode: StatusCodes.Status404NotFound);

            await EmitAuditAsync(auditWriter, observers, currentUser, "Assignment", "Unassigned",
                $"user:{userId} -> {assignmentType.Trim().ToLowerInvariant()}:{targetId}", new Dictionary<string, string>(), ct);
            return Results.NoContent();
        });
    }

    // ── Approval limits ("up to this amount") ───────────────────────

    private static void MapApprovalAuthorities(RouteGroupBuilder group)
    {
        group.MapGet("/approval-authorities/{holderType}/{holder}", async (
            string holderType, string holder, EfApprovalAuthorityStore store, CancellationToken ct) =>
        {
            if (!Enum.TryParse<GrantHolderType>(holderType, ignoreCase: true, out var type))
                return InvalidEnum("holderType", holderType, typeof(GrantHolderType));

            return Results.Ok((await store.ListAsync(type, holder, ct)).Select(ToResponse));
        });

        group.MapPost("/approval-authorities", async (
            ApprovalAuthorityWriteRequest request, ClaimsPrincipal caller,
            EfApprovalAuthorityStore store, IPermissionRegistry registry, IAuthorizationService authorization,
            IApprovalAuthorityEvaluator evaluator, IPrincipalGrantQuerySource callerQuery, IOrgHierarchy hierarchy,
            IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, TimeProvider clock, CancellationToken ct) =>
        {
            if (!Enum.TryParse<GrantHolderType>(request.HolderType, ignoreCase: true, out var holderType))
                return InvalidEnum("holderType", request.HolderType, typeof(GrantHolderType));
            if (string.IsNullOrWhiteSpace(request.Holder))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["holder"] = ["A holder is required."] });
            if (holderType is GrantHolderType.User && !Guid.TryParse(request.Holder, out _))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["holder"] = ["A user limit takes the user id."] });
            if (request.Permission is null || request.Permission.EndsWith(":*", StringComparison.Ordinal))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["permission"] = ["Name one registered permission."] });
            _ = GrantGuards.Expand(registry, [request.Permission], out var unknown);
            if (unknown.Count > 0)
                return GrantGuards.Unknown(unknown);
            if (request.MaxAmount < 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["maxAmount"] = ["A limit cannot be negative."] });
            if (request is { ValidFrom: { } from, ValidUntil: { } until } && until <= from)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["validUntil"] = ["A limit must end after it begins."] });
            if (request.OrgUnitId is { } unit && !hierarchy.Contains(unit))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["orgUnitId"] = [$"Unknown org unit {unit}."] });

            var target = $"{holderType}:{request.Holder}";
            if (GrantGuards.TargetsCaller(caller, holderType, request.Holder))
                return await RefuseAsync(auditWriter, currentUser, "ApprovalAuthority", target,
                    "self-grant", "You cannot set an approval limit for yourself or for a role you hold.", ct);

            // A limit is authority you hand on: you cannot give more than you hold (grant-any lifts the ceiling).
            if (!(await authorization.AuthorizeAsync(caller, GrantAnyPermission)).Succeeded)
            {
                var own = evaluator.Check(callerQuery.Current, request.Permission,
                    new ApprovalContext(request.MaxAmount, request.Currency, request.DocumentType, request.OrgUnitId));
                if (!own.IsWithinAuthority)
                    return await RefuseAsync(auditWriter, currentUser, "ApprovalAuthority", target,
                        "above-ceiling", "You cannot give an approval limit above your own.", ct);
            }

            var saved = await store.AddAsync(
                new ApprovalAuthority(holderType, request.Holder, request.Permission, request.MaxAmount, request.Currency,
                    request.DocumentType, request.OrgUnitId, request.ValidFrom, request.ValidUntil),
                GrantGuards.UserIdOf(caller), clock.GetUtcNow(), ct);

            await EmitAuditAsync(auditWriter, observers, currentUser, "ApprovalAuthority", "Set", target,
                new Dictionary<string, string>
                {
                    ["permission"] = request.Permission,
                    ["maxAmount"] = request.MaxAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["currency"] = request.Currency ?? "",
                    ["documentType"] = request.DocumentType ?? "",
                    ["orgUnitId"] = request.OrgUnitId?.ToString() ?? "",
                    ["validFrom"] = request.ValidFrom?.ToString("O") ?? "",
                    ["validUntil"] = request.ValidUntil?.ToString("O") ?? "",
                }, ct);

            return Results.Created($"approval-authorities/{holderType}/{request.Holder}", ToResponse(saved));
        });

        group.MapDelete("/approval-authorities/{id:guid}", async (
            Guid id, ClaimsPrincipal caller, EfApprovalAuthorityStore store,
            IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (await store.GetAsync(id, ct) is not { } existing)
                return Results.Problem(detail: "Approval limit not found.", statusCode: StatusCodes.Status404NotFound);

            await store.RemoveAsync(id, ct);
            await EmitAuditAsync(auditWriter, observers, currentUser, "ApprovalAuthority", "Removed",
                $"{existing.Authority.HolderType}:{existing.Authority.Holder}",
                new Dictionary<string, string>
                {
                    ["permission"] = existing.Authority.Permission,
                    ["maxAmount"] = existing.Authority.MaxAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                }, ct);
            return Results.NoContent();
        });
    }

    // A typo would sit in the table matching nothing: when scope maps declare assignment types, only those are accepted.
    private static IResult? UnknownAssignmentType(IScopeMapRegistry scopeMaps, string? assignmentType)
    {
        var declared = scopeMaps.AssignmentTypes;
        if (declared.Count == 0 || string.IsNullOrWhiteSpace(assignmentType) || declared.Contains(assignmentType.Trim()))
            return null;

        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["assignmentType"] = [$"Not a declared assignment type: {assignmentType.Trim()}. Declared: {string.Join(", ", declared.Order(StringComparer.OrdinalIgnoreCase))}."],
        });
    }

    private static OrgUnitProfileResponse ToResponse(OrgUnitProfile p)
        => new(p.UnitId, p.Code, p.Name, p.Kind, p.IsClosed, p.ManagerUserId, p.ClosedAt);

    private static ApprovalAuthorityResponse ToResponse(EfApprovalAuthorityStore.Stored s)
        => new(s.Id, s.Authority.HolderType.ToString(), s.Authority.Holder, s.Authority.Permission, s.Authority.MaxAmount,
            s.Authority.Currency, s.Authority.DocumentType, s.Authority.OrgUnitId, s.Authority.ValidFrom, s.Authority.ValidUntil,
            s.CreatedBy, s.CreatedAt);

    private static ScopedGrantResponse ToResponse(EfPermissionGrantStore.ScopedGrantRecord record)
        => new(record.Id, record.Grant.HolderType.ToString(), record.Grant.Holder, record.Grant.Permission,
            record.Grant.Type.ToString(), record.Grant.EffectiveScope.Format(), record.Grant.ValidFrom,
            record.Grant.ValidUntil, record.Grant.Reason, record.CreatedBy, record.CreatedAt);

    // ── Organization ───────────────────────────────────────────────

    private static void MapOrganization(RouteGroupBuilder group)
    {
        group.MapPost("/org/units", async (
            OrgUnitWriteRequest request,
            EfOrgHierarchy hierarchy, IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            await hierarchy.AddUnitAsync(request.Id, request.Parents ?? [], ct);

            await EmitAuditAsync(auditWriter, observers, currentUser, "OrgUnit", "Created",
                $"unit:{request.Id}",
                new Dictionary<string, string> { ["parents"] = string.Join(",", request.Parents ?? []) }, ct);

            return Results.NoContent();
        });

        group.MapPut("/org/units/{id:guid}/parents", async (
            Guid id, OrgUnitParentsRequest request,
            EfOrgHierarchy hierarchy, IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            await hierarchy.MoveUnitAsync(id, request.Parents, ct);

            await EmitAuditAsync(auditWriter, observers, currentUser, "OrgUnit", "Reparented",
                $"unit:{id}",
                new Dictionary<string, string> { ["parents"] = string.Join(",", request.Parents) }, ct);

            return Results.NoContent();
        });

        // ── Company and unit profiles (master data beside the hierarchy) ──

        group.MapGet("/org/company-profile", async (EfOrganizationProfileStore profiles, CancellationToken ct) =>
            await profiles.GetCompanyAsync(ct) is { } profile
                ? Results.Ok(profile)
                : Results.Problem(detail: "No company profile has been saved.", statusCode: StatusCodes.Status404NotFound));

        group.MapPut("/org/company-profile", async (
            CompanyProfileRequest request, EfOrganizationProfileStore profiles,
            IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, TimeProvider clock, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.LegalName))
                errors["legalName"] = ["A legal name is required."];
            if (request.FiscalYearStartMonth is < 1 or > 12)
                errors["fiscalYearStartMonth"] = ["The financial year starts in month 1 to 12."];
            if (request.Currency is { Length: > 0 } currency && currency.Trim().Length != 3)
                errors["currency"] = ["Use a three-letter ISO 4217 code."];
            if (request.Country is { Length: > 0 } country && country.Trim().Length != 2)
                errors["country"] = ["Use a two-letter ISO 3166-1 code."];
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            await profiles.SaveCompanyAsync(
                new CompanyProfile(request.LegalName, request.TradeName, request.RegistrationNumber, request.TaxId, request.Address,
                    request.Country, request.Currency, request.FiscalYearStartMonth ?? 1, request.TimeZone, request.Language),
                clock.GetUtcNow(), ct);
            await EmitAuditAsync(auditWriter, observers, currentUser, "Company", "ProfileSaved", "company",
                new Dictionary<string, string> { ["legalName"] = request.LegalName.Trim(), ["currency"] = request.Currency ?? "" }, ct);
            return Results.NoContent();
        });

        group.MapGet("/org/units", async (EfOrganizationProfileStore profiles, CancellationToken ct) =>
            Results.Ok((await profiles.ListUnitsAsync(ct)).Select(ToResponse)));

        group.MapGet("/org/units/{id:guid}/profile", async (Guid id, EfOrganizationProfileStore profiles, CancellationToken ct) =>
            await profiles.GetUnitAsync(id, ct) is { } profile
                ? Results.Ok(ToResponse(profile))
                : Results.Problem(detail: "No profile for this unit.", statusCode: StatusCodes.Status404NotFound));

        group.MapPut("/org/units/{id:guid}/profile", async (
            Guid id, OrgUnitProfileRequest request, EfOrganizationProfileStore profiles, EfOrgHierarchy hierarchy,
            [FromServices] IUserRoleDirectory? roleDirectory,
            IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, TimeProvider clock, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.Code))
                errors["code"] = ["A code is required."];
            if (string.IsNullOrWhiteSpace(request.Name))
                errors["name"] = ["A name is required."];
            if (string.IsNullOrWhiteSpace(request.Kind))
                errors["kind"] = ["A kind is required (branch, factory, office, warehouse, department, team, ...)."];
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);
            if (!hierarchy.Contains(id))
                return Results.Problem(detail: "The unit does not exist; create it first.", statusCode: StatusCodes.Status404NotFound);
            if (request.ManagerUserId is { } manager && roleDirectory is not null && await roleDirectory.GetRolesAsync(manager, ct) is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["managerUserId"] = ["The manager is not a known user."] });

            var existing = await profiles.GetUnitAsync(id, ct);
            var saved = await profiles.SaveUnitAsync(
                new OrgUnitProfile(id, request.Code, request.Name, request.Kind, existing?.IsClosed ?? false, request.ManagerUserId, existing?.ClosedAt),
                clock.GetUtcNow(), ct);
            if (!saved)
                return Results.Conflict(new { error = "Duplicate code", message = $"Another unit already uses the code '{request.Code.Trim()}'." });

            await EmitAuditAsync(auditWriter, observers, currentUser, "OrgUnit", "ProfileSaved", $"unit:{id}",
                new Dictionary<string, string>
                {
                    ["code"] = request.Code.Trim(),
                    ["kind"] = request.Kind.Trim().ToLowerInvariant(),
                    ["manager"] = request.ManagerUserId?.ToString() ?? "",
                }, ct);
            return Results.NoContent();
        });

        group.MapPost("/org/units/{id:guid}/close", async (
            Guid id, EfOrganizationProfileStore profiles, EfOrgHierarchy hierarchy,
            IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, TimeProvider clock, CancellationToken ct) =>
        {
            if (await profiles.GetUnitAsync(id, ct) is null)
                return Results.Problem(detail: "No profile for this unit.", statusCode: StatusCodes.Status404NotFound);

            // A branch cannot close while something beneath it still operates.
            var below = hierarchy.Descendants(id);
            var open = (await profiles.ListUnitsAsync(ct)).Where(p => !p.IsClosed && p.UnitId != id && below.Contains(p.UnitId)).ToList();
            if (open.Count > 0)
                return Results.Conflict(new { error = "Open units below", message = "Close these units first.", units = open.Select(p => p.Code).ToList() });

            await profiles.SetClosedAsync(id, true, clock.GetUtcNow(), ct);
            await EmitAuditAsync(auditWriter, observers, currentUser, "OrgUnit", "Closed", $"unit:{id}", new Dictionary<string, string>(), ct);
            return Results.NoContent();
        });

        group.MapPost("/org/units/{id:guid}/reopen", async (
            Guid id, EfOrganizationProfileStore profiles, EfOrgHierarchy hierarchy,
            IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, TimeProvider clock, CancellationToken ct) =>
        {
            if (await profiles.GetUnitAsync(id, ct) is null)
                return Results.Problem(detail: "No profile for this unit.", statusCode: StatusCodes.Status404NotFound);

            var above = hierarchy.Ancestors(id);
            var closed = (await profiles.ListUnitsAsync(ct)).Where(p => p.IsClosed && above.Contains(p.UnitId)).ToList();
            if (closed.Count > 0)
                return Results.Conflict(new { error = "Closed units above", message = "Reopen these units first.", units = closed.Select(p => p.Code).ToList() });

            await profiles.SetClosedAsync(id, false, clock.GetUtcNow(), ct);
            await EmitAuditAsync(auditWriter, observers, currentUser, "OrgUnit", "Reopened", $"unit:{id}", new Dictionary<string, string>(), ct);
            return Results.NoContent();
        });

        group.MapGet("/org/placements/{userId:guid}", (
            Guid userId, EfOrgPlacementStore store) =>
            Results.Ok(store.GetPlacements(userId)));

        group.MapPost("/org/placements", async (
            PlacementWriteRequest request,
            EfOrgPlacementStore store, IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var modeToken = request.Mode ?? nameof(OrgScopeMode.UnitAndDescendants);
            if (!Enum.TryParse<OrgScopeMode>(modeToken, ignoreCase: true, out var mode))
                return InvalidEnum("mode", modeToken, typeof(OrgScopeMode));

            await store.PlaceAsync(request.UserId, request.OrgUnitId, mode, ct);

            await EmitAuditAsync(auditWriter, observers, currentUser, "OrgPlacement", "Placed",
                $"user:{request.UserId} -> unit:{request.OrgUnitId}",
                new Dictionary<string, string> { ["mode"] = mode.ToString() }, ct);

            return Results.NoContent();
        });

        group.MapDelete("/org/placements/{userId:guid}/{orgUnitId:guid}", async (
            Guid userId, Guid orgUnitId,
            EfOrgPlacementStore store, IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            await store.RemoveAsync(userId, orgUnitId, ct);

            await EmitAuditAsync(auditWriter, observers, currentUser, "OrgPlacement", "Removed",
                $"user:{userId} -> unit:{orgUnitId}",
                new Dictionary<string, string>(), ct);

            return Results.NoContent();
        });
    }

    // ── Feature entitlements ───────────────────────────────────────

    private static void MapEntitlements(RouteGroupBuilder parent)
    {
        // Plans and per-company overrides are platform-wide data. A company administrator holds authorization:manage too,
        // so these routes also need the host-level permission AND a request made outside any company.
        var group = parent.MapGroup("/features")
            .RequireAuthorization(EntitlementsPermission)
            .AddEndpointFilter(HostOnlyAsync);

        group.MapGet("/plans/{plan}", (
            string plan, EfFeatureEntitlementStore store) =>
            Results.Ok(store.PlanFeatures(plan)));

        group.MapPut("/plans/{plan}", async (
            string plan, PlanDefinitionRequest request,
            EfFeatureEntitlementStore store, IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            await store.DefinePlanAsync(plan, request.Features, ct);

            await EmitAuditAsync(auditWriter, observers, currentUser, "FeatureEntitlement", "PlanDefined",
                $"plan:{plan}",
                new Dictionary<string, string> { ["features"] = string.Join(",", request.Features) }, ct);

            return Results.NoContent();
        });

        group.MapPut("/tenants/{tenantId:guid}/plan", async (
            Guid tenantId, PlanAssignmentRequest request,
            EfFeatureEntitlementStore store, IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            await store.AssignPlanAsync(tenantId, request.Plan, ct);

            await EmitAuditAsync(auditWriter, observers, currentUser, "FeatureEntitlement", "PlanAssigned",
                $"tenant:{tenantId}",
                new Dictionary<string, string> { ["plan"] = request.Plan }, ct);

            return Results.NoContent();
        });

        group.MapPut("/tenants/{tenantId:guid}/overrides/{feature}", async (
            Guid tenantId, string feature, OverrideWriteRequest request,
            EfFeatureEntitlementStore store, IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (request.Enabled)
                await store.EnableAsync(tenantId, feature, ct);
            else
                await store.DisableAsync(tenantId, feature, ct);

            await EmitAuditAsync(auditWriter, observers, currentUser, "FeatureEntitlement", "OverrideSet",
                $"tenant:{tenantId}",
                new Dictionary<string, string> { ["feature"] = feature, ["enabled"] = request.Enabled.ToString() }, ct);

            return Results.NoContent();
        });

        group.MapDelete("/tenants/{tenantId:guid}/overrides/{feature}", async (
            Guid tenantId, string feature,
            EfFeatureEntitlementStore store, IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            await store.ClearOverrideAsync(tenantId, feature, ct);

            await EmitAuditAsync(auditWriter, observers, currentUser, "FeatureEntitlement", "OverrideCleared",
                $"tenant:{tenantId}",
                new Dictionary<string, string> { ["feature"] = feature }, ct);

            return Results.NoContent();
        });
    }

    // ── Delegations ────────────────────────────────────────────────

    private static void MapDelegations(RouteGroupBuilder group)
    {
        // Full listing, active or not — the governance/audit review surface.
        group.MapGet("/delegations", (EfDelegationStore store) =>
            Results.Ok(store.All()));

        group.MapPost("/delegations", async (
            DelegationWriteRequest request,
            EfDelegationStore store, IPermissionRegistry registry, PermissionResolver resolver,
            [FromServices] IUserRoleDirectory? roleDirectory, IOptions<AuthorizationManagementOptions> limits, TimeProvider clock,
            IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (request.Permissions is not { Length: > 0 })
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["permissions"] = ["At least one permission is required."],
                });

            if (request.FromUserId == request.ToUserId)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["toUserId"] = ["A delegation must be to a different user than the delegator."],
                });

            if (request.NotAfter <= request.NotBefore)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["notAfter"] = ["A delegation window must end after it begins."],
                });

            var options = limits.Value;
            if (request.NotAfter - request.NotBefore > options.MaxDelegationDuration)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["notAfter"] = [$"A delegation may cover at most {options.MaxDelegationDuration.TotalDays:0.#} days."],
                });

            if (request.NotAfter <= clock.GetUtcNow())
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["notAfter"] = ["A delegation must end in the future."],
                });

            // Only permissions modules declared; wildcards are not delegable (a delegation names exactly what it lends).
            if (request.Permissions.Any(p => p.EndsWith(":*", StringComparison.Ordinal)))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["permissions"] = ["Wildcards cannot be delegated; name each permission."],
                });
            _ = GrantGuards.Expand(registry, request.Permissions, out var unknown);
            if (unknown.Count > 0)
                return GrantGuards.Unknown(unknown);

            var blocked = request.Permissions
                .Where(p => options.NonDelegablePrefixes.Any(prefix => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            blocked.AddRange(request.Permissions.Where(p => !blocked.Contains(p)
                && registry.GetAll().Any(d => d.Sensitivity is PermissionSensitivity.Critical
                                              && string.Equals(d.Permission, p, StringComparison.OrdinalIgnoreCase))));
            if (blocked.Count > 0)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["permissions"] = [$"Not delegable: {string.Join(", ", blocked)}."],
                });

            // The delegator's roles come from the identity store, never from the request: a caller-supplied list would let
            // anyone invent the authority the delegation is capped by (BR-006). Without a directory the cap falls back to
            // the delegator's direct grants only, which can only be stricter.
            IReadOnlyCollection<string> delegatorRoles = [];
            if (roleDirectory is not null)
            {
                if (await roleDirectory.GetRolesAsync(request.FromUserId, ct) is not { } roles)
                    return Results.Problem(detail: "The delegator is unknown.", statusCode: StatusCodes.Status404NotFound);
                if (await roleDirectory.GetRolesAsync(request.ToUserId, ct) is null)
                    return Results.Problem(detail: "The delegate is unknown.", statusCode: StatusCodes.Status404NotFound);
                delegatorRoles = roles;
            }

            // You cannot lend what you do not hold (FR-GRT-005): checked now, and again at every decision.
            var held = resolver.Resolve(new PrincipalGrantQuery(request.FromUserId, delegatorRoles));
            var notHeld = request.Permissions.Where(p => !held.Contains(p)).ToList();
            if (notHeld.Count > 0)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["permissions"] = [$"The delegator does not hold: {string.Join(", ", notHeld)}."],
                });

            var delegation = await store.DelegateAsync(
                request.FromUserId, delegatorRoles, request.ToUserId,
                request.Permissions, request.NotBefore, request.NotAfter, ct);

            await EmitAuditAsync(auditWriter, observers, currentUser, "Delegation", "Created",
                $"from:{request.FromUserId} -> to:{request.ToUserId}",
                new Dictionary<string, string>
                {
                    ["permissions"] = string.Join(",", request.Permissions),
                    ["notBefore"] = request.NotBefore.ToString("O"),
                    ["notAfter"] = request.NotAfter.ToString("O"),
                }, ct);

            return Results.Created($"delegations/{delegation.Id}", delegation);
        });

        group.MapDelete("/delegations/{id:guid}", async (
            Guid id, EfDelegationStore store, IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!await store.RevokeAsync(id, ct))
                return Results.Problem(
                    detail: "Delegation not found or already revoked.",
                    statusCode: StatusCodes.Status404NotFound);

            await EmitAuditAsync(auditWriter, observers, currentUser, "Delegation", "Revoked",
                $"delegation:{id}", new Dictionary<string, string>(), ct);

            return Results.NoContent();
        });
    }

    // ── Helpers ────────────────────────────────────────────────────

    /// <summary>Refuses a request made inside a company: platform-wide data is for host operators only.</summary>
    private static async ValueTask<object?> HostOnlyAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var tenant = context.HttpContext.RequestServices.GetRequiredService<ICurrentTenant>();
        if (!tenant.IsHost && tenant.TenantId is not null)
            return GrantGuards.Refused("This resource is managed by the platform operator, not from inside a company.");

        return await next(context);
    }

    /// <summary>
    /// Emits an <see cref="AuthorizationAdministrativeChangeEvent"/> after a
    /// management-store write has already succeeded (auth blueprint §5.14/§16 —
    /// administrative changes are always audited). Called on the success path
    /// only: a failed store write never reaches this call, so there is no audit
    /// record for a change that didn't happen.
    /// </summary>
    private static async Task EmitAuditAsync(
        IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
        ICurrentUser currentUser,
        string category,
        string action,
        string targetDescription,
        IReadOnlyDictionary<string, string> details,
        CancellationToken ct)
    {
        await auditWriter.WriteAsync(
            new AuthorizationAdministrativeChangeEvent(
                category, action, currentUser.UserId?.ToString(), targetDescription, details),
            ct);

        // Systems that cache access outside this process (the AI connector) must drop it.
        await observers.NotifyAccessChangedAsync(
            new AccessChange
            {
                Kind = category switch
                {
                    "OrgUnit" or "OrgPlacement" or "Assignment" => AccessChangeKinds.Organization,
                    "FeatureEntitlement" => AccessChangeKinds.Feature,
                    "Delegation" => AccessChangeKinds.Delegation,
                    _ => AccessChangeKinds.Grant,
                },
                Reason = $"{category}.{action}".ToLowerInvariant(),
            },
            ct: ct);
    }

    // ── Governance ───────────────────────────────────────────────

    private static void MapGovernance(RouteGroupBuilder group)
    {
        // GET /authorization/effective-access/{userId} — what can this user do?
        // Optional ?roles=r1&r2 supplies the user's role memberships (the
        // store cannot know Identity membership); without them the report
        // covers direct grants and delegations only.
        group.MapGet("/effective-access/{userId:guid}", async (
            Guid userId,
            string[]? roles,
            IEffectiveAccessService? effectiveAccessService,
            [FromServices] IUserRoleDirectory? roleDirectory, CancellationToken ct) =>
        {
            if (effectiveAccessService is null)
                return Results.NotFound("Effective access service not registered.");

            // The identity store is authoritative; ?roles= is only a what-if when no directory is registered.
            IReadOnlyCollection<string> effectiveRoles = roles ?? [];
            if (roleDirectory is not null)
            {
                if (await roleDirectory.GetRolesAsync(userId, ct) is not { } known)
                    return Results.NotFound("Unknown user.");
                effectiveRoles = known;
            }

            var report = effectiveAccessService.Report(new PrincipalGrantQuery(userId, effectiveRoles));
            return Results.Ok(new
            {
                userId = report.UserId,
                directPermissions = report.DirectPermissions,
                delegatedPermissions = report.DelegatedPermissions.Select(d => new
                {
                    permission = d.Permission,
                    onBehalfOf = d.OnBehalfOf,
                    delegationId = d.DelegationId,
                }),
                allPermissions = report.AllPermissions,
                sodViolations = report.SodViolations.Select(v => new
                {
                    constraint = v.Constraint.Name,
                    rationale = v.Constraint.Rationale,
                    heldPermissions = v.HeldPermissions,
                }),
            });
        })
        .WithSummary("Get effective access for a user")
        .WithName("GetEffectiveAccess");

        // POST /authorization/sod-violations/scan — who's violating SoD?
        // Bulk scan: role membership comes from the IUserRoleDirectory when one is
        // registered; without it the scan covers direct grants and delegations only
        // and role-delivered halves of toxic combinations are invisible to it.
        group.MapPost("/sod-violations/scan", async (
            EfPermissionGrantStore store,
            IEffectiveAccessService? effectiveAccessService,
            ISodPolicy sodPolicy,
            [FromServices] IUserRoleDirectory? roleDirectory,
            CancellationToken ct) =>
        {
            if (effectiveAccessService is null)
                return Results.NotFound("Effective access service not registered.");

            // Scan all users in the grant store
            var allUsers = await store.GetAllUsersWithGrantsAsync(ct);
            var violations = new List<object>();

            foreach (var userId in allUsers)
            {
                IReadOnlyCollection<string> userRoles = roleDirectory is null
                    ? []
                    : await roleDirectory.GetRolesAsync(userId, ct) ?? [];
                var report = effectiveAccessService.Report(new PrincipalGrantQuery(userId, userRoles));
                if (report.SodViolations.Count > 0)
                {
                    violations.Add(new
                    {
                        userId,
                        violations = report.SodViolations.Select(v => new
                        {
                            constraint = v.Constraint.Name,
                            rationale = v.Constraint.Rationale,
                            heldPermissions = v.HeldPermissions,
                        }),
                    });
                }
            }

            return Results.Ok(new
            {
                totalViolations = violations.Count,
                violations,
            });
        })
        .WithSummary("Scan for SoD violations across all users")
        .WithName("ScanSodViolations");

        // GET /authorization/recertifications — list active campaigns
        group.MapGet("/recertifications", async (
            IRecertificationCampaignStore? store,
            CancellationToken ct) =>
        {
            if (store is null)
                return Results.NotFound("Recertification campaign store not registered.");

            var campaigns = await store.ListActiveAsync(ct);
            return Results.Ok(campaigns.Select(c => new
            {
                campaignId = c.Id,
                name = c.Name,
                pendingCount = c.PendingCount,
                totalCount = c.TotalCount,
                progress = c.TotalCount > 0 ? ((c.TotalCount - c.PendingCount) * 100) / c.TotalCount : 100,
            }));
        })
        .WithSummary("List active recertification campaigns")
        .WithName("ListRecertifications");

        // GET /authorization/recertifications/{campaignId} — get campaign details
        group.MapGet("/recertifications/{campaignId:guid}", async (
            Guid campaignId,
            IRecertificationCampaignStore? store,
            CancellationToken ct) =>
        {
            if (store is null)
                return Results.NotFound("Recertification campaign store not registered.");

            var campaign = await store.GetAsync(campaignId, ct);
            if (campaign is null)
                return Results.NotFound();

            return Results.Ok(new
            {
                campaignId,
                name = campaign.Name,
                itemCount = campaign.Items.Count,
                pendingCount = campaign.Pending.Count,
                revokedCount = campaign.Revoked.Count,
                isComplete = campaign.IsComplete,
                items = campaign.Items.Select(i => new
                {
                    userId = i.UserId,
                    permission = i.Permission,
                    source = i.Source.ToString(),
                    decision = i.Decision.ToString(),
                }),
            });
        })
        .WithSummary("Get campaign details")
        .WithName("GetRecertification");

        // POST /authorization/recertifications — create campaign
        group.MapPost("/recertifications", async (
            RecertificationCreateRequest request,
            IEffectiveAccessService? effectiveAccessService,
            IRecertificationCampaignStore? store,
            ICurrentUser currentUser,
            IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            CancellationToken ct) =>
        {
            if (effectiveAccessService is null)
                return Results.BadRequest("Effective access service not registered.");
            if (store is null)
                return Results.BadRequest("Recertification campaign store not registered.");

            var userIds = request.UserIds ?? [];
            var reports = new List<EffectiveAccessReport>();

            // Role membership comes from the caller (user id → roles); the
            // store cannot know Identity membership. Users absent from the map
            // are reported on direct grants and delegations only.
            foreach (var userId in userIds)
            {
                string[] roles = [];
                if (request.UserRoles?.TryGetValue(userId.ToString(), out var r) == true && r is not null)
                    roles = r;
                reports.Add(effectiveAccessService.Report(new PrincipalGrantQuery(userId, roles)));
            }

            var campaign = new RecertificationCampaign(request.Name, reports);
            var createdBy = currentUser.UserId ?? Guid.Empty;
            var campaignId = await store.CreateAsync(request.Name,
                campaign.Items.ToList(), createdBy, ct);

            await EmitAuditAsync(auditWriter, observers, currentUser, "Recertification", "Created",
                $"campaign:{campaignId}",
                new Dictionary<string, string> { ["itemCount"] = campaign.Items.Count.ToString() }, ct);

            return Results.Created($"/authorization/recertifications/{campaignId}",
                new { campaignId, itemCount = campaign.Items.Count });
        })
        .WithSummary("Create recertification campaign")
        .WithName("CreateRecertification");

        // POST /authorization/recertifications/{campaignId}/review — review one item
        group.MapPost("/recertifications/{campaignId:guid}/review", async (
            Guid campaignId,
            RecertificationReviewRequest request,
            IRecertificationCampaignStore? store,
            ICurrentUser currentUser,
            IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            CancellationToken ct) =>
        {
            if (store is null)
                return Results.NotFound("Recertification campaign store not registered.");

            var campaign = await store.GetAsync(campaignId, ct);
            if (campaign is null)
                return Results.NotFound();

            if (!Enum.TryParse<RecertificationDecision>(request.Decision, ignoreCase: true, out var decision))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["decision"] = ["Must be 'Certified' or 'Revoked'"],
                });

            var reviewedBy = currentUser.UserId ?? Guid.Empty;
            await store.UpdateDecisionAsync(campaignId, request.UserId, request.Permission,
                decision, reviewedBy, ct);

            await EmitAuditAsync(auditWriter, observers, currentUser, "Recertification", "Reviewed",
                $"campaign:{campaignId}",
                new Dictionary<string, string>
                {
                    ["userId"] = request.UserId.ToString(),
                    ["permission"] = request.Permission,
                    ["decision"] = decision.ToString(),
                }, ct);

            return Results.NoContent();
        })
        .WithSummary("Review one access line in a campaign")
        .WithName("ReviewRecertification");

        // POST /authorization/recertifications/{campaignId}/complete — mark campaign done
        group.MapPost("/recertifications/{campaignId:guid}/complete", async (
            Guid campaignId,
            IRecertificationCampaignStore? store,
            ICurrentUser currentUser,
            IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            CancellationToken ct) =>
        {
            if (store is null)
                return Results.NotFound("Recertification campaign store not registered.");

            var campaign = await store.GetAsync(campaignId, ct);
            if (campaign is null)
                return Results.NotFound();

            if (!campaign.IsComplete)
                return Results.BadRequest(new { error = "Campaign has pending reviews" });

            await store.CompleteAsync(campaignId, ct);

            await EmitAuditAsync(auditWriter, observers, currentUser, "Recertification", "Completed",
                $"campaign:{campaignId}",
                new Dictionary<string, string>
                {
                    ["revokedCount"] = campaign.Revoked.Count.ToString(),
                }, ct);

            return Results.NoContent();
        })
        .WithSummary("Mark campaign as complete")
        .WithName("CompleteRecertification");
    }

    // Request/response models for recertification
    private sealed record RecertificationCreateRequest(
        string Name, Guid[]? UserIds, Dictionary<string, string[]>? UserRoles = null);
    private sealed record RecertificationReviewRequest(Guid UserId, string Permission, string Decision);

    private static Guid ParseUser(string holder)
        => Guid.TryParse(holder, out var id)
            ? id
            : throw new BadHttpRequestException("User holder must be a GUID user id.");

    private static IResult InvalidEnum(string field, string value, Type enumType)
        => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [field] =
                [$"'{value}' is not one of: {string.Join(", ", Enum.GetNames(enumType))}."],
        });
}
