namespace Modulus.MultiTenancy.EntityFrameworkCore;

using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Abstractions.Security;

/// <summary>Body of <c>POST /api/tenancy/tenants</c>: a new company's short name, its display name and its group.</summary>
/// <param name="Slug">The short name: lower-case letters, digits and hyphens, starting with a letter or digit.</param>
/// <param name="DisplayName">The name shown to people.</param>
/// <param name="GroupId">The group of companies to put the company in, if any.</param>
/// <param name="AdministratorUserId">The account to make a member of the new company, if any (the designated administrator).</param>
public sealed record TenantCreateRequest(string Slug, string? DisplayName, Guid? GroupId, Guid? AdministratorUserId = null);

/// <summary>Body of <c>PUT /api/tenancy/tenants/{id}/status</c>.</summary>
/// <param name="Status">Trial, Active, Suspended or Closed.</param>
/// <param name="TrialEndsAt">When the trial ends; required for Trial, ignored otherwise.</param>
public sealed record TenantStatusRequest(string Status, DateTimeOffset? TrialEndsAt);

/// <summary>Body of <c>PUT /api/tenancy/tenants/{id}/seats</c>: the seat limit, or null to remove it.</summary>
/// <param name="MaxUsers">The most active members the company may have.</param>
public sealed record TenantSeatsRequest(int? MaxUsers);

/// <summary>Body of <c>PUT /api/tenancy/tenants/{id}/parent</c>: the parent company, or null to make the company top-level.</summary>
/// <param name="ParentId">The parent company's id.</param>
public sealed record TenantParentRequest(Guid? ParentId);

/// <summary>Body of <c>POST /api/tenancy/tenants/{id}/members</c>.</summary>
/// <param name="UserId">The account to make a member.</param>
public sealed record TenantMemberRequest(Guid UserId);

/// <summary>Body of <c>POST /api/tenancy/tenants/{parentId}/children/{childId}/authority</c>: the account and the operation it may perform in the child.</summary>
/// <param name="PrincipalId">The account that receives the authority. It cannot be the administrator who grants it.</param>
/// <param name="Operation"><c>ManageMembers</c> or <c>ManageGrants</c>.</param>
public sealed record CompanyAuthorityRequest(Guid PrincipalId, string Operation);

/// <summary>
/// The company administration API (premodule 2): list and read companies, create one, change its status and seats, and manage its members.
/// It runs in the host context (a company administrator cannot reach another company's record through it), and every route needs
/// <see cref="ManagePermission"/>, which is Critical. The rules (suspended is read-only, closed does not resolve, the seat limit) are
/// enforced by <see cref="TenantManager{TTenant,TMembership,TDataStore}"/>; these routes only map its answers to HTTP.
/// </summary>
public static partial class TenantAdminEndpoints
{
    /// <summary>The permission that guards the routes.</summary>
    public const string ManagePermission = "tenants:manage";

    private static readonly Regex SlugPattern = SlugRegex();

    /// <summary>Registers the permission. Requires <c>AddModulusAuthorization</c> for it to be enforced.</summary>
    public static IServiceCollection AddModulusTenantAdmin(this IServiceCollection services)
    {
        services.AddPermissions("Modulus.Tenancy.Companies", registry =>
            registry.Add(
                ManagePermission,
                "Create companies, change their status and seat limit, and manage their members.",
                null,
                PermissionSensitivity.Critical));
        services.AddPermissions("Modulus.Tenancy.Companies", registry =>
            registry.Add(
                ParentCompanyAuthority.ChildrenManagePermission,
                "Manage the members and grants of a child company, where the explicit parent-to-child authority allows it (decision 6).",
                null,
                PermissionSensitivity.Critical));
        services.TryAddScoped<ParentCompanyAuthority>();
        return services;
    }

    /// <summary>
    /// Maps <c>GET</c> and <c>POST</c> on <paramref name="prefix"/> (default <c>/api/tenancy/tenants</c>), <c>GET {id}</c>,
    /// <c>PUT {id}/status</c>, <c>PUT {id}/seats</c>, <c>PUT {id}/parent</c>, <c>GET {id}/members</c>, <c>POST {id}/members</c> and
    /// <c>DELETE {id}/members/{userId}</c>, and <c>POST {id}/baseline</c> (re-applies the company baseline).
    /// The authority routes (<c>{id}/children/{childId}/authority</c>) are host-only. The parent-to-child routes
    /// (<c>{parentId}/children/{childId}/members</c> and <c>.../baseline</c>) need an explicit authority; see <see cref="ParentCompanyAuthority"/>.
    /// </summary>
    public static RouteGroupBuilder MapModulusTenantAdmin<TTenant, TMembership, TDataStore>(this IEndpointRouteBuilder endpoints, string prefix = "/api/tenancy/tenants")
        where TTenant : TenantEntity, new()
        where TMembership : TenantMembershipEntity, new()
        where TDataStore : TenantDataStoreEntity
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix).RequireAuthorization(ManagePermission);

        group.MapGet("/", async (string? search, int? page, int? pageSize, TenantManager<TTenant, TMembership, TDataStore> manager, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            var number = Math.Max(page ?? 1, 1);
            var size = Math.Clamp(pageSize ?? 50, 1, 200);
            var (items, total) = await manager.ListAsync(search, number, size, ct);
            return Results.Ok(new { items, total, page = number, pageSize = size });
        });

        group.MapPost("/", async (TenantCreateRequest body, HttpContext http, TenantManager<TTenant, TMembership, TDataStore> manager, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            var slug = body.Slug?.Trim().ToLowerInvariant() ?? "";
            if (!SlugPattern.IsMatch(slug))
                return Problem("slug", "A company name is 1 to 63 lower-case letters, digits or hyphens, starting with a letter or digit.");
            // Checked before anything is created: a designated administrator that is not an account would leave a company nobody can join.
            if (body.AdministratorUserId is { } designated && !await IsAccountAsync(http, designated, ct))
                return Problem("administratorUserId", "The designated administrator cannot be verified as an account in the identity store.");
            TenantInfo created;
            try
            {
                created = await manager.CreateAsync(slug, string.IsNullOrWhiteSpace(body.DisplayName) ? null : body.DisplayName.Trim(), null, body.GroupId, ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = "DUPLICATE_TENANT", message = ex.Message });
            }

            // D-10: the configured baseline first, so the designated administrator never meets a company without its grants. An invalid
            // baseline leaves the company created with no grants (no one can use it yet); the response says so.
            if (http.RequestServices.GetService<ICompanyProvisioner>() is { } provisioner)
            {
                try
                {
                    await provisioner.ProvisionAsync(created, ct);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Json(new { error = "BASELINE_INVALID", message = ex.Message, companyId = created.TenantId }, statusCode: StatusCodes.Status500InternalServerError);
                }
            }

            if (body.AdministratorUserId is { } administrator)
                await manager.AddMemberAsync(administrator, created.TenantId, ct);

            var view = await manager.GetAsync(created.TenantId, ct);
            return Results.Created($"{prefix}/{created.TenantId:D}", view);
        });

        // Re-applies the baseline to an existing company. Roles that already hold grants there are left alone, so only the roles the company
        // has no grants for receive them; a deliberate change is never overwritten. Answers the roles that were granted now.
        group.MapPost("/{id:guid}/baseline", async (Guid id, HttpContext http, ITenantStore store, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            if (http.RequestServices.GetService<ICompanyProvisioner>() is not { } provisioner)
                return Results.Json(new { error = "BASELINE_UNAVAILABLE", message = "No company provisioner is registered." }, statusCode: StatusCodes.Status503ServiceUnavailable);
            var company = await store.FindByIdAsync(id, ct);
            if (company is null)
                return Results.NotFound();

            try
            {
                var granted = await provisioner.ProvisionAsync(company, ct);
                return Results.Ok(new { companyId = company.TenantId, grantedRoles = granted });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = "BASELINE_INVALID", message = ex.Message, companyId = company.TenantId }, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        group.MapGet("/{id:guid}", async (Guid id, TenantManager<TTenant, TMembership, TDataStore> manager, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            return await manager.GetAsync(id, ct) is { } view ? Results.Ok(view) : Results.NotFound();
        });

        group.MapPut("/{id:guid}/status", async (Guid id, TenantStatusRequest body, TenantManager<TTenant, TMembership, TDataStore> manager, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            if (!Enum.TryParse<TenantStatus>(body.Status, ignoreCase: true, out var status) || !Enum.IsDefined(status))
                return Problem("status", "The status is Trial, Active, Suspended or Closed.");
            try
            {
                if (!await manager.SetStatusAsync(id, status, body.TrialEndsAt, ct))
                    return Results.NotFound();
                return Results.Ok(await manager.GetAsync(id, ct));
            }
            catch (ArgumentException ex)
            {
                return Problem(ex.ParamName ?? "status", ex.Message);
            }
        });

        group.MapPut("/{id:guid}/seats", async (Guid id, TenantSeatsRequest body, TenantManager<TTenant, TMembership, TDataStore> manager, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            if (body.MaxUsers is < 0)
                return Problem("maxUsers", "The seat limit cannot be negative.");
            if (!await manager.SetMaxUsersAsync(id, body.MaxUsers, ct))
                return Results.NotFound();
            return Results.Ok(await manager.GetAsync(id, ct));
        });

        group.MapPut("/{id:guid}/parent", async (Guid id, TenantParentRequest body, TenantManager<TTenant, TMembership, TDataStore> manager, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            try
            {
                if (!await manager.SetParentAsync(id, body.ParentId, ct))
                    return Results.NotFound();
                return Results.Ok(await manager.GetAsync(id, ct));
            }
            catch (ArgumentException ex)
            {
                return Problem(ex.ParamName ?? "parentId", ex.Message);
            }
        });

        group.MapGet("/{id:guid}/members", async (Guid id, TenantManager<TTenant, TMembership, TDataStore> manager, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            if (await manager.GetAsync(id, ct) is null)
                return Results.NotFound();
            return Results.Ok(await manager.ListMembersAsync(id, ct));
        });

        group.MapPost("/{id:guid}/members", async (Guid id, TenantMemberRequest body, TenantManager<TTenant, TMembership, TDataStore> manager, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            if (body.UserId == Guid.Empty)
                return Problem("userId", "A user is required.");
            try
            {
                return await manager.AddMemberAsync(body.UserId, id, ct) ? Results.NoContent() : Results.NotFound();
            }
            catch (ConflictException ex)
            {
                return Results.Conflict(new { error = "SEAT_LIMIT", message = ex.Message });
            }
        });

        group.MapDelete("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, TenantManager<TTenant, TMembership, TDataStore> manager, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            return await manager.RemoveMemberAsync(userId, id, ct) ? Results.NoContent() : Results.NotFound();
        });

        // The explicit parent-to-child authorities (decision 6). Only the host administrator (ManagePermission above) may grant or revoke
        // them, and nobody grants authority to themselves. A grant is read directly, never cached.
        group.MapGet("/{id:guid}/children/{childId:guid}/authority", async (Guid id, Guid childId, TenantManager<TTenant, TMembership, TDataStore> manager, HttpContext http, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            if (http.RequestServices.GetService<ICompanyAuthorityStore>() is not { } authorities)
                return AuthorityStoreUnavailable();
            if (await manager.GetAsync(childId, ct) is not { } child || child.ParentId != id)
                return Results.NotFound();
            return Results.Ok((await authorities.ListAsync(id, childId, ct)).Select(AuthorityView));
        });

        group.MapPost("/{id:guid}/children/{childId:guid}/authority", async (Guid id, Guid childId, CompanyAuthorityRequest body, ClaimsPrincipal caller, TenantManager<TTenant, TMembership, TDataStore> manager, HttpContext http, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            if (http.RequestServices.GetService<ICompanyAuthorityStore>() is not { } authorities)
                return AuthorityStoreUnavailable();
            if (!Enum.TryParse<CompanyOperation>(body.Operation, ignoreCase: true, out var operation) || !Enum.IsDefined(operation))
                return Problem("operation", "The operation is ManageMembers or ManageGrants.");
            if (body.PrincipalId == Guid.Empty)
                return Problem("principalId", "An account is required.");

            var grantor = ParentCompanyAuthority.UserIdOf(caller);
            if (grantor == body.PrincipalId)
                return Results.Json(new { error = "SELF_AUTHORITY", message = "An administrator cannot grant company authority to themselves." }, statusCode: StatusCodes.Status403Forbidden);
            if (await manager.GetAsync(childId, ct) is not { } child)
                return Results.NotFound();
            if (child.ParentId != id)
                return Problem("childId", "That company is not a child of the parent company.");

            var grant = await authorities.GrantAsync(id, childId, body.PrincipalId, operation, grantor, DateTimeOffset.UtcNow, ct);
            RecordAudit(http, "company-authority.granted", SecurityAuditOutcomes.Success, childId, grantor, $"tenant:{childId:D} parent={id:D} principal={body.PrincipalId:D} operation={operation}");
            return Results.Created($"{prefix}/{id:D}/children/{childId:D}/authority/{grant.Id:D}", AuthorityView(grant));
        });

        group.MapDelete("/{id:guid}/children/{childId:guid}/authority/{grantId:guid}", async (Guid id, Guid childId, Guid grantId, ClaimsPrincipal caller, HttpContext http, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            if (http.RequestServices.GetService<ICompanyAuthorityStore>() is not { } authorities)
                return AuthorityStoreUnavailable();
            var grant = await authorities.FindAsync(grantId, ct);
            if (grant is null || grant.ParentId != id || grant.ChildId != childId)
                return Results.NotFound();

            var revoker = ParentCompanyAuthority.UserIdOf(caller);
            if (!await authorities.RevokeAsync(grantId, revoker, DateTimeOffset.UtcNow, ct))
                return Results.NotFound();
            RecordAudit(http, "company-authority.revoked", SecurityAuditOutcomes.Success, childId, revoker, $"tenant:{childId:D} parent={id:D} principal={grant.PrincipalId:D} operation={grant.Operation}");
            return Results.NoContent();
        });

        // Parent-to-child administration. Each route runs the checks of ParentCompanyAuthority (valid relationship, the parent permission,
        // a matching active authority for the operation) before it changes anything; a refusal is audited and answers 403 without detail.
        var children = endpoints.MapGroup(prefix).RequireAuthorization();

        children.MapPost("/{parentId:guid}/children/{childId:guid}/members", async (Guid parentId, Guid childId, TenantMemberRequest body, ClaimsPrincipal caller, TenantManager<TTenant, TMembership, TDataStore> manager, HttpContext http, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            var decision = await CheckChildAsync(http, caller, parentId, childId, CompanyOperation.ManageMembers, ct);
            if (!decision.Allowed)
                return Refused(http, caller, parentId, childId, decision);
            if (body.UserId == Guid.Empty)
                return Problem("userId", "A user is required.");
            try
            {
                return await manager.AddMemberAsync(body.UserId, childId, ct) ? Results.NoContent() : Results.NotFound();
            }
            catch (ConflictException ex)
            {
                return Results.Conflict(new { error = "SEAT_LIMIT", message = ex.Message });
            }
        });

        children.MapDelete("/{parentId:guid}/children/{childId:guid}/members/{userId:guid}", async (Guid parentId, Guid childId, Guid userId, ClaimsPrincipal caller, TenantManager<TTenant, TMembership, TDataStore> manager, HttpContext http, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            var decision = await CheckChildAsync(http, caller, parentId, childId, CompanyOperation.ManageMembers, ct);
            if (!decision.Allowed)
                return Refused(http, caller, parentId, childId, decision);
            return await manager.RemoveMemberAsync(userId, childId, ct) ? Results.NoContent() : Results.NotFound();
        });

        // Re-applies the child's baseline. A grant operation, so it needs a ManageGrants authority (a ManageMembers one does not cover it).
        children.MapPost("/{parentId:guid}/children/{childId:guid}/baseline", async (Guid parentId, Guid childId, ClaimsPrincipal caller, ITenantStore store, HttpContext http, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            var decision = await CheckChildAsync(http, caller, parentId, childId, CompanyOperation.ManageGrants, ct);
            if (!decision.Allowed)
                return Refused(http, caller, parentId, childId, decision);
            if (http.RequestServices.GetService<ICompanyProvisioner>() is not { } provisioner)
                return Results.Json(new { error = "BASELINE_UNAVAILABLE", message = "No company provisioner is registered." }, statusCode: StatusCodes.Status503ServiceUnavailable);
            if (await store.FindByIdAsync(childId, ct) is not { } company)
                return Results.NotFound();

            try
            {
                var granted = await provisioner.ProvisionAsync(company, ct);
                return Results.Ok(new { companyId = company.TenantId, grantedRoles = granted });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = "BASELINE_INVALID", message = ex.Message, companyId = company.TenantId }, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        return group;
    }

    /// <summary>
    /// Runs the parent-to-child checks. The service is resolved here rather than as a route parameter, so an application that maps the
    /// routes without registering <c>AddModulusTenantAdmin</c> still starts; without it every child route refuses (fail closed).
    /// </summary>
    private static Task<CompanyAuthorityDecision> CheckChildAsync(HttpContext http, ClaimsPrincipal caller, Guid parentId, Guid childId, CompanyOperation operation, CancellationToken ct)
        => http.RequestServices.GetService<ParentCompanyAuthority>() is { } authority
            ? authority.CheckAsync(caller, parentId, childId, operation, ct)
            : Task.FromResult(CompanyAuthorityDecision.Deny("AUTHORITY_UNAVAILABLE"));

    private static object AuthorityView(CompanyAuthorityGrant grant)
        => new
        {
            id = grant.Id,
            parentId = grant.ParentId,
            childId = grant.ChildId,
            principalId = grant.PrincipalId,
            operation = grant.Operation.ToString(),
            grantedBy = grant.GrantedBy,
            grantedAt = grant.GrantedAt,
            revokedAt = grant.RevokedAt,
            revokedBy = grant.RevokedBy,
            isActive = grant.IsActive,
        };

    private static IResult AuthorityStoreUnavailable()
        => Results.Json(new { error = "AUTHORITY_STORE_UNAVAILABLE", message = "No company authority store is registered." }, statusCode: StatusCodes.Status503ServiceUnavailable);

    /// <summary>Answers a refused parent-to-child request: audited with the rule that refused it, and 403 without detail.</summary>
    private static IResult Refused(HttpContext http, ClaimsPrincipal caller, Guid parentId, Guid childId, CompanyAuthorityDecision decision)
    {
        RecordAudit(http, "company-authority.denied", SecurityAuditOutcomes.Denied, childId, ParentCompanyAuthority.UserIdOf(caller),
            $"tenant:{childId:D} parent={parentId:D} reason={decision.Reason}");
        return Results.Json(new { error = "CHILD_AUTHORITY_DENIED", message = "The caller has no authority over this company." }, statusCode: StatusCodes.Status403Forbidden);
    }

    private static void RecordAudit(HttpContext http, string action, string outcome, Guid companyId, Guid? actor, string target)
        => http.RequestServices.GetService<ISecurityAuditLog>()?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Tenancy,
            Action = action,
            Outcome = outcome,
            TenantId = companyId,
            Actor = actor?.ToString("D"),
            Target = target,
        });

    /// <summary>
    /// Whether <paramref name="userId"/> is an account in the identity store. Refuses when no identity store is registered, since an
    /// account that cannot be verified must not be made a company's administrator.
    /// </summary>
    private static async Task<bool> IsAccountAsync(HttpContext http, Guid userId, CancellationToken ct)
    {
        var directory = http.RequestServices.GetService<IUserRoleDirectory>();
        return directory is not null && await directory.GetRolesAsync(userId, ct) is not null;
    }

    private static IResult Problem(string field, string message)
        => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,62}$")]
    private static partial Regex SlugRegex();
}
