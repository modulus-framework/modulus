using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Fields;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Abstractions.Security;
using Modulus.Identity.Abstractions;

namespace Modulus.Identity.Extensions;

/// <summary>
/// One row of the user list. Personal fields are classified, so the field guard masks them for a caller without
/// <see cref="AccountLifecycleExtensions.PersonalDataPermission"/>.
/// </summary>
public sealed record UserSummary(
    Guid Id,
    string? UserName,
    [property: Classified(FieldClassification.Restricted)] string? Email,
    [property: Classified(FieldClassification.Restricted)] string? FullName,
    bool IsActive,
    string Lifecycle,
    DateTimeOffset? LockedUntil);

/// <summary>One user, with the state an administrator needs. Never a password hash, a PIN or a secret.</summary>
public sealed record UserDetail(
    Guid Id,
    string? UserName,
    [property: Classified(FieldClassification.Restricted)] string? Email,
    bool EmailConfirmed,
    [property: Classified(FieldClassification.Restricted)] string? FirstName,
    [property: Classified(FieldClassification.Restricted)] string? LastName,
    [property: Classified(FieldClassification.Restricted)] string? PhoneNumber,
    [property: Classified(FieldClassification.Restricted)] string? EmployeeCode,
    bool IsActive,
    string Lifecycle,
    DateTimeOffset? LockedUntil,
    bool TwoFactorEnabled,
    IReadOnlyList<string> Roles);

/// <summary>A page of the user list.</summary>
public sealed record UserPage(IReadOnlyList<UserSummary> Items, int Total, int Page, int PageSize);

/// <summary>Body of <c>PUT {id}/roles</c>: the complete set of roles the user should have.</summary>
public sealed record UserRolesRequest(IReadOnlyList<string> Roles);

/// <summary>Body of <c>POST {id}/lock</c>: until when, and why. A null <c>Until</c> locks the account until someone unlocks it.</summary>
public sealed record UserLockRequest(DateTimeOffset? Until, string Reason);

/// <summary>Body of <c>POST invitations</c>: the person to invite, the roles they get, and their optional name fields.</summary>
/// <param name="Email">The address the invitation link is sent to.</param>
/// <param name="Roles">The roles the account gets. The caller must hold the permissions they carry.</param>
/// <param name="FirstName">The first name, if known.</param>
/// <param name="LastName">The last name, if known.</param>
/// <param name="UserName">The user name; the address is used when empty.</param>
public sealed record UserInvitationRequest(string Email, IReadOnlyList<string>? Roles, string? FirstName, string? LastName, string? UserName);

/// <summary>
/// The user administration premodule (premodule 1 in <c>docs/PREMODULES_PLAN.md</c>): list and read users, set a user's roles, and lock
/// and unlock an account. Every route needs <see cref="AccountLifecycleExtensions.ManagePermission"/> (<c>identity:accounts:manage</c>, Critical),
/// so an anonymous caller gets <c>401</c>, a caller without the permission gets <c>403</c>, and the step-up policy applies when it is on.
/// Every write is recorded in the security audit with the actor and the target.
/// </summary>
public static class UserAdminExtensions
{
    /// <summary>The largest page the list returns.</summary>
    public const int MaxPageSize = 100;

    /// <summary>The most accounts one export may hold.</summary>
    private const int MaxExportRows = 5000;

    /// <summary>The longest reason a lock or a role change may carry.</summary>
    public const int MaxReasonLength = 500;

    /// <summary>Maps the user administration routes under <paramref name="prefix"/> (default <c>/identity/users</c>).</summary>
    public static RouteGroupBuilder MapModulusUserAdmin<TUser>(this IEndpointRouteBuilder endpoints, string prefix = "/identity/users")
        where TUser : ModulusUser
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix).RequireAuthorization(AccountLifecycleExtensions.ManagePermission);

        // Filters: search (user name, and e-mail for holders of the personal-data permission), status (active, inactive or locked) and role.
        group.MapGet("/", async (string? search, string? status, string? role, int? page, int? pageSize, UserManager<TUser> users, ICurrentUser currentUser, IFieldAuthorizer authorizer, CancellationToken ct) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, MaxPageSize);
            var number = Math.Max(page ?? 1, 1);
            if (!IsKnownStatus(status))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["The status is active, inactive, locked or any."] });
            var query = await FilterUsersAsync(users, currentUser, search, status, role, ct);

            var total = query.Count();
            var rows = query.OrderBy(u => u.NormalizedUserName)
                .Skip((number - 1) * size)
                .Take(size)
                .ToList();
            var items = rows.Select(Summary).ToList();
            return Results.Ok(authorizer.Redact(new UserPage(items, total, number, size)));
        });

        // The filtered accounts as CSV, with the detail's masking: a caller without the personal-data permission gets blank name, phone and
        // employee code cells. Capped, so a large export is a narrower query. The export is audited with its row count and no values.
        group.MapGet("/export", async (string? search, string? status, string? role, UserManager<TUser> users, ICurrentUser currentUser, IFieldAuthorizer authorizer, ClaimsPrincipal caller, HttpContext http, CancellationToken ct) =>
        {
            if (ActorOf(caller) is not { } actor)
                return Results.Unauthorized();
            if (!IsKnownStatus(status))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["The status is active, inactive, locked or any."] });
            var query = await FilterUsersAsync(users, currentUser, search, status, role, ct);
            var total = query.Count();
            if (total > MaxExportRows)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["export"] = [$"{total} accounts match; an export holds at most {MaxExportRows}. Narrow the filter."] });

            var rows = query.OrderBy(u => u.NormalizedUserName).ToList();
            var lines = new List<string> { "userName,email,firstName,lastName,phoneNumber,employeeCode,isActive,lifecycle,lockedUntil,roles" };
            foreach (var user in rows)
            {
                var detail = await DetailAsync(user, users, authorizer);
                lines.Add(string.Join(',', new[]
                {
                    detail.UserName, detail.Email, detail.FirstName, detail.LastName, detail.PhoneNumber, detail.EmployeeCode,
                    detail.IsActive ? "true" : "false", detail.Lifecycle, detail.LockedUntil?.ToString("O"), string.Join(';', detail.Roles),
                }.Select(CsvCell)));
            }

            http.RequestServices.GetService<ISecurityAuditLog>()?.Record(new SecurityAuditEvent
            {
                Category = SecurityAuditCategories.Identity,
                Action = "identity.user.exported",
                Actor = actor.ToString("D"),
                Target = "users",
                Details = new Dictionary<string, string?> { ["rows"] = rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            });
            return Results.Text(string.Join('\n', lines) + "\n", "text/csv");
        });

        group.MapGet("/{id:guid}", async (Guid id, UserManager<TUser> users, IFieldAuthorizer authorizer, HttpContext http, CancellationToken ct) =>
        {
            var user = await users.FindByIdAsync(id.ToString());
            if (user is null)
                return Results.NotFound();
            http.Response.Headers.ETag = ETagOf(user);
            return Results.Ok(await DetailAsync(user, users, authorizer));
        });

        // What the account can do now: its direct grants plus the grants of its roles, as the authorization engine would decide. The
        // report lists permission names only, so it shows no personal data.
        group.MapGet("/{id:guid}/effective-access", async (Guid id, UserManager<TUser> users, IEffectiveAccessService? access, CancellationToken ct) =>
        {
            if (access is null)
                return Results.NotFound(new { error = "EFFECTIVE_ACCESS_UNAVAILABLE", message = "Effective access is not registered (AddModulusAuthorization)." });
            var user = await users.FindByIdAsync(id.ToString());
            if (user is null)
                return Results.NotFound();
            var roles = await users.GetRolesAsync(user);
            var report = access.Report(new PrincipalGrantQuery(id, roles.ToList()));
            return Results.Ok(new
            {
                direct = report.DirectPermissions.Order(StringComparer.Ordinal).ToList(),
                all = report.AllPermissions.Order(StringComparer.Ordinal).ToList(),
                delegated = report.DelegatedPermissions.Select(d => new { permission = d.Permission, onBehalfOf = d.OnBehalfOf }).ToList(),
                sodViolations = report.SodViolations.Count,
            });
        });

        // Invites a person into the current company: the account waits for an e-mailed link and has no password until it is used. The service
        // checks the roles and the field clearance; a refusal (a duplicate, an unknown role) is a 400 with the reason.
        group.MapPost("/invitations", async (UserInvitationRequest body, IUserInvitationService invitations, ICurrentTenant tenant, ClaimsPrincipal caller, HttpContext http, CancellationToken ct) =>
        {
            if (ActorOf(caller) is not { } actor)
                return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(body.Email))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["An e-mail address is required."] });

            var result = await invitations.InviteAsync(new UserInvitation(body.Email, body.Roles, tenant.TenantId, body.FirstName, body.LastName, body.UserName), ct);
            if (!result.Succeeded)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["invitation"] = [result.Error!] });

            Audit(http, actor, "identity.user.invited", result.UserId!.Value, $"roles={string.Join(',', body.Roles ?? [])}");
            return Results.Created($"{prefix}/{result.UserId:D}", new { id = result.UserId, email = result.Email });
        });

        // The account's sessions (tokens with their client and times) and its recent sign-in attempts, for an administrator. Neither carries
        // personal fields. A session can be ended here; ending all of them is the identity service's RevokeAllAsync, audited by it.
        group.MapGet("/{id:guid}/sessions", async (Guid id, UserManager<TUser> users, IUserSessionService? sessions, CancellationToken ct) =>
        {
            if (sessions is null)
                return Results.NotFound(new { error = "SESSIONS_UNAVAILABLE", message = "Session management is not registered (AddModulusIdentity)." });
            if (await users.FindByIdAsync(id.ToString()) is null)
                return Results.NotFound();
            return Results.Ok(await sessions.ListAsync(id, ct));
        });

        group.MapDelete("/{id:guid}/sessions/{sessionId}", async (Guid id, string sessionId, UserManager<TUser> users, IUserSessionService? sessions, ClaimsPrincipal caller, HttpContext http, CancellationToken ct) =>
        {
            if (ActorOf(caller) is not { } actor)
                return Results.Unauthorized();
            if (sessions is null)
                return Results.NotFound(new { error = "SESSIONS_UNAVAILABLE", message = "Session management is not registered (AddModulusIdentity)." });
            if (await users.FindByIdAsync(id.ToString()) is null)
                return Results.NotFound();
            if (!await sessions.RevokeAsync(id, sessionId, ct))
                return Results.NotFound();

            Audit(http, actor, "identity.user.session.revoked", id, $"session={sessionId}");
            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/login-history", async (Guid id, UserManager<TUser> users, ILoginHistoryService? history, CancellationToken ct) =>
        {
            if (history is null)
                return Results.NotFound(new { error = "LOGIN_HISTORY_UNAVAILABLE", message = "Login history is not registered (AddModulusIdentity)." });
            if (await users.FindByIdAsync(id.ToString()) is null)
                return Results.NotFound();
            return Results.Ok(await history.GetAsync(id, 50, ct));
        });

        group.MapPut("/{id:guid}/roles", async (Guid id, UserRolesRequest body, UserManager<TUser> users, ClaimsPrincipal caller, HttpContext http, IFieldAuthorizer authorizer, CancellationToken ct) =>
        {
            if (ActorOf(caller) is not { } actor)
                return Results.Unauthorized();
            var user = await users.FindByIdAsync(id.ToString());
            if (user is null)
                return Results.NotFound();

            if (IsStale(http, user))
                return Stale();
            var current = await users.GetRolesAsync(user);
            var desired = body.Roles.Select(r => r.Trim()).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var toAdd = desired.Except(current, StringComparer.OrdinalIgnoreCase).ToList();
            var toRemove = current.Except(desired, StringComparer.OrdinalIgnoreCase).ToList();

            if (toAdd.Count > 0)
            {
                var added = await users.AddToRolesAsync(user, toAdd);
                if (!added.Succeeded)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["roles"] = added.Errors.Select(e => e.Description).ToArray() });
            }
            if (toRemove.Count > 0)
            {
                var removed = await users.RemoveFromRolesAsync(user, toRemove);
                if (!removed.Succeeded)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["roles"] = removed.Errors.Select(e => e.Description).ToArray() });
            }

            // Role changes do not move the concurrency stamp by themselves; a plain update does, so the next If-Match sees the change.
            var stamped = await users.UpdateAsync(user);
            if (!stamped.Succeeded)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["roles"] = stamped.Errors.Select(e => e.Description).ToArray() });

            Audit(http, actor, "identity.user.roles.changed", user.Id, $"added={string.Join(',', toAdd)};removed={string.Join(',', toRemove)}");
            http.Response.Headers.ETag = ETagOf(user);
            return Results.Ok(await DetailAsync(user, users, authorizer));
        });

        group.MapPost("/{id:guid}/lock", async (Guid id, UserLockRequest body, UserManager<TUser> users, ClaimsPrincipal caller, HttpContext http, IFieldAuthorizer authorizer, CancellationToken ct) =>
        {
            if (ActorOf(caller) is not { } actor)
                return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length > MaxReasonLength)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = [$"A reason of 1 to {MaxReasonLength} characters is required."] });
            if (body.Until is { } until && until <= DateTimeOffset.UtcNow)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["until"] = ["The lock must end in the future."] });
            var user = await users.FindByIdAsync(id.ToString());
            if (user is null)
                return Results.NotFound();

            if (IsStale(http, user))
                return Stale();
            await users.SetLockoutEnabledAsync(user, true);
            var set = await users.SetLockoutEndDateAsync(user, body.Until ?? DateTimeOffset.MaxValue);
            if (!set.Succeeded)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["lock"] = set.Errors.Select(e => e.Description).ToArray() });

            Audit(http, actor, "identity.user.locked", user.Id, $"until={(body.Until?.ToString("O") ?? "indefinite")};reason={body.Reason.Trim()}");
            http.Response.Headers.ETag = ETagOf(user);
            return Results.Ok(await DetailAsync(user, users, authorizer));
        });

        group.MapPost("/{id:guid}/unlock", async (Guid id, UserManager<TUser> users, ClaimsPrincipal caller, HttpContext http, IFieldAuthorizer authorizer, CancellationToken ct) =>
        {
            if (ActorOf(caller) is not { } actor)
                return Results.Unauthorized();
            var user = await users.FindByIdAsync(id.ToString());
            if (user is null)
                return Results.NotFound();

            if (IsStale(http, user))
                return Stale();
            var cleared = await users.SetLockoutEndDateAsync(user, null);
            if (!cleared.Succeeded)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["lock"] = cleared.Errors.Select(e => e.Description).ToArray() });

            Audit(http, actor, "identity.user.unlocked", user.Id, null);
            http.Response.Headers.ETag = ETagOf(user);
            return Results.Ok(await DetailAsync(user, users, authorizer));
        });

        return group;
    }

    /// <summary>The filters the list and the export share: search by user name (and e-mail for holders of the personal-data permission), status and role.</summary>
    private static async Task<IQueryable<TUser>> FilterUsersAsync<TUser>(UserManager<TUser> users, ICurrentUser currentUser, string? search, string? status, string? role, CancellationToken ct)
        where TUser : ModulusUser
    {
        var query = users.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Matching on email would reveal an address to a caller who cannot read it, so only holders of the personal-data permission match it.
            var term = search.Trim().ToUpperInvariant();
            var canMatchEmail = currentUser.HasPermission(AccountLifecycleExtensions.PersonalDataPermission);
            query = query.Where(u => (u.NormalizedUserName != null && u.NormalizedUserName.Contains(term))
                || (canMatchEmail && u.NormalizedEmail != null && u.NormalizedEmail.Contains(term)));
        }
        switch (status?.Trim().ToLowerInvariant())
        {
            case "active":
                var blocked = await LockedIdsAsync(users, ct);
                query = query.Where(u => u.IsActive && !blocked.Contains(u.Id));
                break;
            case "inactive":
                query = query.Where(u => !u.IsActive);
                break;
            case "locked":
                var locked = await LockedIdsAsync(users, ct);
                query = query.Where(u => locked.Contains(u.Id));
                break;
        }
        if (!string.IsNullOrWhiteSpace(role))
        {
            // Role membership comes from the identity store, so the filter reads the role's members and matches them by id.
            var members = (await users.GetUsersInRoleAsync(role.Trim())).Select(u => u.Id).ToHashSet();
            query = query.Where(u => members.Contains(u.Id));
        }
        return query;
    }

    /// <summary>One CSV cell: quoted when it holds a separator, and a cell a spreadsheet would read as a formula is kept as text.</summary>
    private static string CsvCell(string? value)
    {
        var text = value ?? "";
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0]))
            text = "'" + text;
        return text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }

    private static bool IsKnownStatus(string? status)
        => status?.Trim().ToLowerInvariant() is null or "" or "any" or "active" or "inactive" or "locked";

    /// <summary>
    /// The accounts locked right now. The provider cannot compare a nullable <c>DateTimeOffset</c> with the clock in SQL on every
    /// database (SQLite cannot), so only the accounts that have a lock-out end are read and compared here; that set is small.
    /// </summary>
    private static async Task<HashSet<Guid>> LockedIdsAsync<TUser>(UserManager<TUser> users, CancellationToken ct) where TUser : ModulusUser
    {
        var now = DateTimeOffset.UtcNow;
        var withEnd = await users.Users.Where(u => u.LockoutEnd != null).Select(u => new { u.Id, u.LockoutEnd }).ToListAsync(ct);
        return withEnd.Where(u => u.LockoutEnd > now).Select(u => u.Id).ToHashSet();
    }

    /// <summary>The ETag of a user: its concurrency stamp, quoted as RFC 9110 requires.</summary>
    private static string ETagOf<TUser>(TUser user) where TUser : ModulusUser => $"\"{user.ConcurrencyStamp}\"";

    /// <summary>True when the request carries <c>If-Match</c> and none of its values names the user's current state (or <c>*</c>).</summary>
    private static bool IsStale<TUser>(HttpContext http, TUser user) where TUser : ModulusUser
    {
        var values = http.Request.Headers["If-Match"].ToArray();
        return values.Length > 0 && !values.Contains("*") && !values.Contains(ETagOf(user));
    }

    private static IResult Stale()
        => Results.Json(new { error = "STALE_WRITE", message = "The account changed since you read it. Read it again and retry." }, statusCode: StatusCodes.Status412PreconditionFailed);

    /// <summary>The acting person, from the authenticated principal's subject claim. Null when the principal has no subject.</summary>
    private static Guid? ActorOf(ClaimsPrincipal caller)
        => Guid.TryParse(caller.FindFirstValue("sub") ?? caller.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static UserSummary Summary<TUser>(TUser user) where TUser : ModulusUser
        => new(user.Id, user.UserName, user.Email, user.FullName, user.IsActive, user.Lifecycle.ToString(),
            user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow ? end : null);

    private static async Task<UserDetail> DetailAsync<TUser>(TUser user, UserManager<TUser> users, IFieldAuthorizer authorizer) where TUser : ModulusUser
    {
        var roles = await users.GetRolesAsync(user);
        return authorizer.Redact(new UserDetail(
            user.Id,
            user.UserName,
            user.Email,
            user.EmailConfirmed,
            user.FirstName,
            user.LastName,
            user.PhoneNumber,
            user.EmployeeCode,
            user.IsActive,
            user.Lifecycle.ToString(),
            user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow ? end : null,
            user.TwoFactorEnabled,
            roles.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList()));
    }

    private static void Audit(HttpContext http, Guid actor, string action, Guid target, string? details)
    {
        var values = new Dictionary<string, string?>();
        if (details is not null)
            values["detail"] = details;
        http.RequestServices.GetService<ISecurityAuditLog>()?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Identity,
            Action = action,
            Actor = actor.ToString("D"),
            Target = $"user:{target:D}",
            Details = values,
        });
    }
}
