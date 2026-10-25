using System.Security.Claims;

namespace Modulus.Identity;

using Microsoft.AspNetCore.Http;
using Modulus.Authorization.Grants;
using Modulus.BackgroundJobs;
using Modulus.Core.Abstractions;

/// <summary>
/// Bridges <see cref="ICurrentUser.HasPermission"/> to the server-side grant store:
/// resolves the current principal's effective permission set from its <em>identity</em>
/// (user id + role claims) rather than trusting fine-grained "permission" claims on
/// the token (blueprint §22). Registered as scoped so the resolved set is computed at
/// most once per request. Fail-closed: an unauthenticated principal resolves to no
/// permissions.
/// </summary>
internal sealed class GrantStorePermissionChecker(
    IHttpContextAccessor httpContextAccessor,
    IPermissionResolver resolver,
    JobContext? job = null) : IPermissionChecker
{
    private IReadOnlySet<string>? _effective;

    public bool HasPermission(string permission)
        => (_effective ??= Resolve()).Contains(permission);

    public IReadOnlyCollection<string> GetEffectivePermissions()
        => _effective ??= Resolve();

    private IReadOnlySet<string> Resolve()
    {
        // Outside a request (a background job, a consumer) there is no principal. A job runs as the person who queued it,
        // with the roles the identity store holds for them when it started (JobContext). A system job has no person and gets nothing.
        if (httpContextAccessor.HttpContext is null)
            return ResolveForJob();

        var principal = httpContextAccessor.HttpContext.User;
        if (principal?.Identity?.IsAuthenticated != true)
            return EmptySet;

        // An agent token whose agent cannot be named grants nothing; otherwise the agent's role caps the person's access (A9-1).
        var agent = principal.FindFirst(AgentClaim)?.Value;
        if (agent is not null && string.IsNullOrWhiteSpace(agent))
            return EmptySet;

        return resolver.Resolve(new PrincipalGrantQuery(ReadUserId(principal), ReadRoles(principal), agent));
    }

    private IReadOnlySet<string> ResolveForJob()
    {
        if (job?.InitiatorId is not { } userId)
            return EmptySet;

        return resolver.Resolve(new PrincipalGrantQuery(userId, job.Roles.ToArray(), job.AgentId));
    }

    // The RFC 8693 actor claim: its value is the agent's role (see the PermissionRequirementHandler, which reads the same claim).
    private const string AgentClaim = "act";

    private static Guid? ReadUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? principal.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    // Accept BOTH the long and short role claim types: principals built from
    // tokens validated with MapInboundClaims=false (OpenIddict, most OIDC
    // library defaults) keep the literal "role" name, while ASP.NET-normalised
    // identities carry ClaimTypes.Role.
    private static IReadOnlyCollection<string> ReadRoles(ClaimsPrincipal principal)
        => principal.Claims
            .Where(c => c.Type is ClaimTypes.Role or "role")
            .Select(c => c.Value)
            .Distinct()
            .ToArray();

    private static readonly IReadOnlySet<string> EmptySet =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
