using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Security;
using Modulus.Identity.Abstractions;

namespace Modulus.Identity.Extensions;

/// <summary>One permission the application declares, as an administrator sees it.</summary>
/// <param name="Permission">The permission name.</param>
/// <param name="Description">What the permission allows.</param>
/// <param name="Sensitivity">Normal, Sensitive or Critical.</param>
/// <param name="Lifecycle">Active, or deprecated with a replacement.</param>
/// <param name="ReplacedBy">The permission that replaces a deprecated one, never granted automatically.</param>
/// <param name="Group">The part of the name before the first colon, e.g. <c>identity</c>.</param>
/// <param name="Requires">Permissions this one implies.</param>
public sealed record PermissionSummary(string Permission, string Description, string Sensitivity, string Lifecycle, string? ReplacedBy, string Group, IReadOnlyList<string> Requires);

/// <summary>One role.</summary>
public sealed record RoleSummary(string Name, string? DisplayName, bool IsDefault);

/// <summary>Body of <c>PUT /roles/{name}</c>: the role's new name.</summary>
public sealed record RoleRenameRequest(string Name);

/// <summary>Body of <c>POST /roles</c>: a new role's name, and an optional display name.</summary>
public sealed record RoleCreateRequest(string Name, string? DisplayName);

/// <summary>
/// The permission catalog and the role list (premodule 1, decision D-P2 and D-P1). The catalog is read-only and lists every permission the
/// modules registered. Roles can be listed and created; deleting a role waits for the check that its grants are cleared, which lives in
/// the authorization management assembly. Both need <see cref="AccountLifecycleExtensions.ManagePermission"/>.
/// </summary>
public static class RoleAdminExtensions
{
    /// <summary>The name prefix reserved for machine clients; no person can hold such a role.</summary>
    public const string ReservedRolePrefix = IntegrationClients.RolePrefix;

    /// <summary>The longest role name the store accepts.</summary>
    public const int MaxRoleNameLength = 256;

    /// <summary>
    /// The built-in administrator role. It is never deleted or renamed, whether or not accounts hold it, so the installation keeps one
    /// role that can administer the rest (decision 6). Its permissions stay editable through the grant routes.
    /// </summary>
    internal const string BuiltInRoleName = "Admin";

    /// <summary>Maps <c>GET {prefix}</c>, the permission catalog (default <c>/api/identity/permissions</c>).</summary>
    public static RouteGroupBuilder MapModulusPermissionCatalog(this IEndpointRouteBuilder endpoints, string prefix = "/api/identity/permissions")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix).RequireAuthorization(AccountLifecycleExtensions.ManagePermission);

        group.MapGet("/", (IPermissionRegistry registry) => Results.Ok(registry.GetAll()
            .OrderBy(p => p.Permission, StringComparer.Ordinal)
            .Select(p => new PermissionSummary(
                p.Permission,
                p.Description,
                p.Sensitivity.ToString(),
                p.Lifecycle.ToString(),
                p.ReplacedBy,
                p.Permission.Split(':')[0],
                p.Requires))
            .ToList()));

        return group;
    }

    /// <summary>Maps <c>GET {prefix}</c> (the roles) and <c>POST {prefix}</c> (create a role), default <c>/api/identity/roles</c>.</summary>
    public static RouteGroupBuilder MapModulusRoleAdmin<TRole>(this IEndpointRouteBuilder endpoints, string prefix = "/api/identity/roles")
        where TRole : ModulusRole, new()
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix).RequireAuthorization(AccountLifecycleExtensions.ManagePermission);

        group.MapGet("/", (RoleManager<TRole> roles) => Results.Ok(roles.Roles
            .OrderBy(r => r.NormalizedName)
            .Select(r => new RoleSummary(r.Name ?? "", r.DisplayName, r.IsDefault))
            .ToList()));

        group.MapGet("/{name}", async (string name, RoleManager<TRole> roles, HttpContext http) =>
        {
            if (await roles.FindByNameAsync(name) is not { } role)
                return Results.NotFound();
            http.Response.Headers.ETag = ETagOf(role);
            return Results.Ok(new RoleSummary(role.Name ?? "", role.DisplayName, role.IsDefault));
        });

        group.MapPost("/", async (RoleCreateRequest body, RoleManager<TRole> roles, HttpContext http, CancellationToken ct) =>
        {
            if (http.User.FindFirst("sub") is null)
                return Results.Unauthorized();
            var name = body.Name?.Trim() ?? "";
            if (name.Length is 0 or > MaxRoleNameLength)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [$"A role name of 1 to {MaxRoleNameLength} characters is required."] });
            if (name.StartsWith(ReservedRolePrefix, StringComparison.OrdinalIgnoreCase))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [$"The prefix '{ReservedRolePrefix}' is reserved for integration clients."] });
            if (await roles.RoleExistsAsync(name))
                return Results.Conflict(new { error = "DUPLICATE_ROLE", message = $"The role '{name}' already exists." });

            var created = await roles.CreateAsync(new TRole { Name = name, DisplayName = string.IsNullOrWhiteSpace(body.DisplayName) ? null : body.DisplayName.Trim() });
            if (!created.Succeeded)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = created.Errors.Select(e => e.Description).ToArray() });

            Audit(http, "identity.role.created", name);
            return Results.Created($"{prefix}/{Uri.EscapeDataString(name)}", new RoleSummary(name, body.DisplayName, false));
        });

        return group;
    }

    /// <summary>
    /// Maps <c>DELETE {prefix}/{name}</c> and <c>PUT {prefix}/{name}</c> (rename, default <c>/api/identity/roles</c>). A role is deleted or
    /// renamed only when no account holds it, it is not the built-in administrator role, it is not the default role, it is not an
    /// integration role, and it holds no permission grant
    /// (<see cref="IRoleDeletionGuard"/>). A rename therefore never leaves a grant or a membership pointing at the old name. Without a
    /// registered guard no role is changed. Needs <see cref="AccountLifecycleExtensions.ManagePermission"/>, as the other role routes do.
    /// </summary>
    public static void MapModulusRoleDelete<TRole, TUser>(this IEndpointRouteBuilder endpoints, string prefix = "/api/identity/roles")
        where TRole : ModulusRole, new()
        where TUser : ModulusUser
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        // Every role change needs the account administration permission, the same as the list and create routes. The per-route checks
        // above protect the data; this one decides who may act at all.
        var group = endpoints.MapGroup(prefix).RequireAuthorization(AccountLifecycleExtensions.ManagePermission);
        group.MapDelete("/{name}", async (string name, RoleManager<TRole> roles, UserManager<TUser> users, IRoleDeletionGuard? guard, HttpContext http, CancellationToken ct) =>
        {
            if (http.User.FindFirst("sub") is null)
                return Results.Unauthorized();
            if (await RefusalAsync(name, roles, users, guard, ct) is { } refusal)
                return refusal;
            var role = (await roles.FindByNameAsync(name))!;
            if (IsStale(http, role))
                return Stale();

            var deleted = await roles.DeleteAsync(role);
            if (!deleted.Succeeded)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = deleted.Errors.Select(e => e.Description).ToArray() });

            Audit(http, "identity.role.deleted", name);
            return Results.NoContent();
        });

        group.MapPut("/{name}", async (string name, RoleRenameRequest body, RoleManager<TRole> roles, UserManager<TUser> users, IRoleDeletionGuard? guard, HttpContext http, CancellationToken ct) =>
        {
            if (http.User.FindFirst("sub") is null)
                return Results.Unauthorized();
            var newName = body.Name?.Trim() ?? "";
            if (newName.Length is 0 or > MaxRoleNameLength)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [$"A role name of 1 to {MaxRoleNameLength} characters is required."] });
            if (newName.StartsWith(ReservedRolePrefix, StringComparison.OrdinalIgnoreCase))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [$"The prefix '{ReservedRolePrefix}' is reserved for integration clients."] });
            if (await RefusalAsync(name, roles, users, guard, ct) is { } refusal)
                return refusal;
            if (!string.Equals(name, newName, StringComparison.OrdinalIgnoreCase) && await roles.RoleExistsAsync(newName))
                return Results.Conflict(new { error = "DUPLICATE_ROLE", message = $"The role '{newName}' already exists." });

            var role = (await roles.FindByNameAsync(name))!;
            if (IsStale(http, role))
                return Stale();
            var renamed = await roles.SetRoleNameAsync(role, newName);
            if (renamed.Succeeded)
                renamed = await roles.UpdateAsync(role);
            if (!renamed.Succeeded)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = renamed.Errors.Select(e => e.Description).ToArray() });

            Audit(http, "identity.role.renamed", name, $"to={newName}");
            http.Response.Headers.ETag = ETagOf(role);
            return Results.Ok(new RoleSummary(newName, role.DisplayName, role.IsDefault));
        });
    }

    /// <summary>The reason a role may not be deleted or renamed, as the response to send; null when it may be changed.</summary>
    private static async Task<IResult?> RefusalAsync<TRole, TUser>(string name, RoleManager<TRole> roles, UserManager<TUser> users, IRoleDeletionGuard? guard, CancellationToken ct)
        where TRole : ModulusRole, new()
        where TUser : ModulusUser
    {
        if (name.StartsWith(ReservedRolePrefix, StringComparison.OrdinalIgnoreCase))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [$"The prefix '{ReservedRolePrefix}' is reserved for integration clients."] });
        var role = await roles.FindByNameAsync(name);
        if (role is null)
            return Results.NotFound();
        if (string.Equals(role.Name, BuiltInRoleName, StringComparison.OrdinalIgnoreCase))
            return Results.Conflict(new { error = "BUILT_IN_ROLE", message = $"The built-in {BuiltInRoleName} role cannot be deleted or renamed." });
        if (role.IsDefault)
            return Results.Conflict(new { error = "DEFAULT_ROLE", message = "The default role cannot be deleted or renamed." });

        var holders = (await users.GetUsersInRoleAsync(name)).Count;
        if (holders > 0)
            return Results.Conflict(new { error = "ROLE_IN_USE", message = $"{holders} account(s) still hold the role. Remove it from them first." });
        if (guard is null)
            return Results.Conflict(new { error = "GRANT_CHECK_UNAVAILABLE", message = "No grant check is registered, so the role cannot be changed safely." });
        if (await guard.BlockingReasonAsync(name, ct) is { } blocking)
            return Results.Conflict(new { error = "ROLE_HAS_GRANTS", message = blocking });
        return null;
    }

    /// <summary>The ETag of a role: its concurrency stamp, quoted as RFC 9110 requires.</summary>
    private static string ETagOf<TRole>(TRole role) where TRole : ModulusRole => $"\"{role.ConcurrencyStamp}\"";

    /// <summary>True when the request carries <c>If-Match</c> and none of its values names the role's current state (or <c>*</c>).</summary>
    private static bool IsStale<TRole>(HttpContext http, TRole role) where TRole : ModulusRole
    {
        var values = http.Request.Headers["If-Match"].ToArray();
        return values.Length > 0 && !values.Contains("*") && !values.Contains(ETagOf(role));
    }

    private static IResult Stale()
        => Results.Json(new { error = "STALE_WRITE", message = "The role changed since you read it. Read it again and retry." }, statusCode: StatusCodes.Status412PreconditionFailed);

    private static void Audit(HttpContext http, string action, string role, string? details = null)
        => http.RequestServices.GetService<ISecurityAuditLog>()?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Identity,
            Action = action,
            Actor = http.User.FindFirst("sub")?.Value,
            Target = $"role:{role}",
            Details = details is null ? new Dictionary<string, string?>() : new Dictionary<string, string?> { ["detail"] = details },
        });
}
