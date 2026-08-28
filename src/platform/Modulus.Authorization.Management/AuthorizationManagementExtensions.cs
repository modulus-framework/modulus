using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Authorization.Audit;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Organization;
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

        // Every mutating endpoint resolves ICurrentUser to attribute the audit
        // event (blueprint §5.14/§16). Normally registered by AddModulus/
        // AddMediator/AddModulusIdentity — TryAdd so this package doesn't
        // require any of those specifically, matching the framework's own
        // fail-safe-default convention for this seam.
        services.TryAddScoped<ICurrentUser, NullCurrentUser>();

        return services.AddPermissions("Modulus.Authorization", registry =>
            registry.Add(
                ManagePermission,
                "Manage authorization data: grants, org structure, feature entitlements, and delegations."));
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
        MapOrganization(group);
        MapEntitlements(group);
        MapDelegations(group);
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
            GrantWriteRequest request,
            EfPermissionGrantStore store, IAuthorizationAuditWriter auditWriter,
            ICurrentUser currentUser, CancellationToken ct) =>
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

            var allow = grantType is PermissionGrantType.Allow;
            if (holderType is GrantHolderType.Role)
                await (allow
                    ? store.GrantToRoleAsync(request.Holder, request.Permissions, ct)
                    : store.DenyToRoleAsync(request.Holder, request.Permissions, ct));
            else
                await (allow
                    ? store.GrantToUserAsync(ParseUser(request.Holder), request.Permissions, ct)
                    : store.DenyToUserAsync(ParseUser(request.Holder), request.Permissions, ct));

            await EmitAuditAsync(auditWriter, currentUser, "Grant", allow ? "Granted" : "Denied",
                $"{holderType}:{request.Holder}",
                new Dictionary<string, string> { ["permissions"] = string.Join(",", request.Permissions) }, ct);

            return Results.NoContent();
        });

        group.MapDelete("/grants/{holderType}/{holder}/{permission}", async (
            string holderType, string holder, string permission,
            EfPermissionGrantStore store, IAuthorizationAuditWriter auditWriter,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!Enum.TryParse<GrantHolderType>(holderType, ignoreCase: true, out var type))
                return InvalidEnum("holderType", holderType, typeof(GrantHolderType));

            if (type is GrantHolderType.Role)
                await store.RevokeFromRoleAsync(holder, permission, ct);
            else
                await store.RevokeFromUserAsync(ParseUser(holder), permission, ct);

            await EmitAuditAsync(auditWriter, currentUser, "Grant", "Revoked",
                $"{holderType}:{holder}",
                new Dictionary<string, string> { ["permission"] = permission }, ct);

            return Results.NoContent();
        });
    }

    // ── Organization ───────────────────────────────────────────────

    private static void MapOrganization(RouteGroupBuilder group)
    {
        group.MapPost("/org/units", async (
            OrgUnitWriteRequest request,
            EfOrgHierarchy hierarchy, IAuthorizationAuditWriter auditWriter,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            await hierarchy.AddUnitAsync(request.Id, request.Parents ?? [], ct);

            await EmitAuditAsync(auditWriter, currentUser, "OrgUnit", "Created",
                $"unit:{request.Id}",
                new Dictionary<string, string> { ["parents"] = string.Join(",", request.Parents ?? []) }, ct);

            return Results.NoContent();
        });

        group.MapPut("/org/units/{id:guid}/parents", async (
            Guid id, OrgUnitParentsRequest request,
            EfOrgHierarchy hierarchy, IAuthorizationAuditWriter auditWriter,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            await hierarchy.MoveUnitAsync(id, request.Parents, ct);

            await EmitAuditAsync(auditWriter, currentUser, "OrgUnit", "Reparented",
                $"unit:{id}",
                new Dictionary<string, string> { ["parents"] = string.Join(",", request.Parents) }, ct);

            return Results.NoContent();
        });

        group.MapGet("/org/placements/{userId:guid}", (
            Guid userId, EfOrgPlacementStore store) =>
            Results.Ok(store.GetPlacements(userId)));

        group.MapPost("/org/placements", async (
            PlacementWriteRequest request,
            EfOrgPlacementStore store, IAuthorizationAuditWriter auditWriter,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var modeToken = request.Mode ?? nameof(OrgScopeMode.UnitAndDescendants);
            if (!Enum.TryParse<OrgScopeMode>(modeToken, ignoreCase: true, out var mode))
                return InvalidEnum("mode", modeToken, typeof(OrgScopeMode));

            await store.PlaceAsync(request.UserId, request.OrgUnitId, mode, ct);

            await EmitAuditAsync(auditWriter, currentUser, "OrgPlacement", "Placed",
                $"user:{request.UserId} -> unit:{request.OrgUnitId}",
                new Dictionary<string, string> { ["mode"] = mode.ToString() }, ct);

            return Results.NoContent();
        });

        group.MapDelete("/org/placements/{userId:guid}/{orgUnitId:guid}", async (
            Guid userId, Guid orgUnitId,
            EfOrgPlacementStore store, IAuthorizationAuditWriter auditWriter,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            await store.RemoveAsync(userId, orgUnitId, ct);

            await EmitAuditAsync(auditWriter, currentUser, "OrgPlacement", "Removed",
                $"user:{userId} -> unit:{orgUnitId}",
                new Dictionary<string, string>(), ct);

            return Results.NoContent();
        });
    }

    // ── Feature entitlements ───────────────────────────────────────

    private static void MapEntitlements(RouteGroupBuilder group)
    {
        group.MapGet("/features/plans/{plan}", (
            string plan, EfFeatureEntitlementStore store) =>
            Results.Ok(store.PlanFeatures(plan)));

        group.MapPut("/features/plans/{plan}", async (
            string plan, PlanDefinitionRequest request,
            EfFeatureEntitlementStore store, IAuthorizationAuditWriter auditWriter,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            await store.DefinePlanAsync(plan, request.Features, ct);

            await EmitAuditAsync(auditWriter, currentUser, "FeatureEntitlement", "PlanDefined",
                $"plan:{plan}",
                new Dictionary<string, string> { ["features"] = string.Join(",", request.Features) }, ct);

            return Results.NoContent();
        });

        group.MapPut("/features/tenants/{tenantId:guid}/plan", async (
            Guid tenantId, PlanAssignmentRequest request,
            EfFeatureEntitlementStore store, IAuthorizationAuditWriter auditWriter,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            await store.AssignPlanAsync(tenantId, request.Plan, ct);

            await EmitAuditAsync(auditWriter, currentUser, "FeatureEntitlement", "PlanAssigned",
                $"tenant:{tenantId}",
                new Dictionary<string, string> { ["plan"] = request.Plan }, ct);

            return Results.NoContent();
        });

        group.MapPut("/features/tenants/{tenantId:guid}/overrides/{feature}", async (
            Guid tenantId, string feature, OverrideWriteRequest request,
            EfFeatureEntitlementStore store, IAuthorizationAuditWriter auditWriter,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (request.Enabled)
                await store.EnableAsync(tenantId, feature, ct);
            else
                await store.DisableAsync(tenantId, feature, ct);

            await EmitAuditAsync(auditWriter, currentUser, "FeatureEntitlement", "OverrideSet",
                $"tenant:{tenantId}",
                new Dictionary<string, string> { ["feature"] = feature, ["enabled"] = request.Enabled.ToString() }, ct);

            return Results.NoContent();
        });

        group.MapDelete("/features/tenants/{tenantId:guid}/overrides/{feature}", async (
            Guid tenantId, string feature,
            EfFeatureEntitlementStore store, IAuthorizationAuditWriter auditWriter,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            await store.ClearOverrideAsync(tenantId, feature, ct);

            await EmitAuditAsync(auditWriter, currentUser, "FeatureEntitlement", "OverrideCleared",
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
            EfDelegationStore store, IAuthorizationAuditWriter auditWriter,
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

            var delegation = await store.DelegateAsync(
                request.FromUserId, request.FromRoles, request.ToUserId,
                request.Permissions, request.NotBefore, request.NotAfter, ct);

            await EmitAuditAsync(auditWriter, currentUser, "Delegation", "Created",
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
            Guid id, EfDelegationStore store, IAuthorizationAuditWriter auditWriter,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!await store.RevokeAsync(id, ct))
                return Results.Problem(
                    detail: "Delegation not found or already revoked.",
                    statusCode: StatusCodes.Status404NotFound);

            await EmitAuditAsync(auditWriter, currentUser, "Delegation", "Revoked",
                $"delegation:{id}", new Dictionary<string, string>(), ct);

            return Results.NoContent();
        });
    }

    // ── Helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Emits an <see cref="AuthorizationAdministrativeChangeEvent"/> after a
    /// management-store write has already succeeded (auth blueprint §5.14/§16 —
    /// administrative changes are always audited). Called on the success path
    /// only: a failed store write never reaches this call, so there is no audit
    /// record for a change that didn't happen.
    /// </summary>
    private static Task EmitAuditAsync(
        IAuthorizationAuditWriter auditWriter,
        ICurrentUser currentUser,
        string category,
        string action,
        string targetDescription,
        IReadOnlyDictionary<string, string> details,
        CancellationToken ct)
        => auditWriter.WriteAsync(
            new AuthorizationAdministrativeChangeEvent(
                category, action, currentUser.UserId?.ToString(), targetDescription, details),
            ct);

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
