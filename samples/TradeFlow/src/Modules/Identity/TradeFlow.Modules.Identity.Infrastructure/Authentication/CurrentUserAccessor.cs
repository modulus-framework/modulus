using Microsoft.AspNetCore.Http;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using TradeFlow.Modules.Identity.Domain.Constants;
using TradeFlow.Shared.Domain.ValueObjects;

namespace TradeFlow.Modules.Identity.Infrastructure.Authentication;

internal sealed class CurrentUserAccessor(
    IHttpContextAccessor httpContextAccessor,
    IPermissionResolver permissionResolver) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            string? userIdClaim = httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.UserId)?.Value
                ?? httpContextAccessor.HttpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(userIdClaim, out Guid userId) ? userId : null;
        }
    }

    public string? UserName => httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.UserName)?.Value;
    public string? Email => httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Email)?.Value;
    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
    public bool IsInRole(string role) => httpContextAccessor.HttpContext?.User?.IsInRole(role) ?? false;

    // The effective permission set is the union of the fine-grained "permission"
    // claims issued with the token AND the server-resolved set from the framework's
    // IPermissionResolver (grants + delegation). The resolver half is what makes
    // runtime-managed authority — grants and delegations created through the
    // /authorization management API — take effect without re-issuing tokens.
    public bool HasPermission(string permission)
        => ClaimPermissions().Contains(permission) || ResolvedPermissions().Contains(permission);

    public IReadOnlyList<string> Permissions
    {
        get
        {
            var claims = ClaimPermissions();
            var resolved = ResolvedPermissions();
            if (resolved.Count == 0)
                return claims;

            var combined = new HashSet<string>(claims, StringComparer.OrdinalIgnoreCase);
            combined.UnionWith(resolved);
            return combined.ToList();
        }
    }

    private IReadOnlySet<string>? _resolved;

    private IReadOnlySet<string> ResolvedPermissions()
    {
        if (_resolved is not null)
            return _resolved;

        IReadOnlySet<string> result;
        try
        {
            var roles = httpContextAccessor.HttpContext?.User?
                .FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? [];
            result = UserId is { } userId
                ? permissionResolver.Resolve(new PrincipalGrantQuery(userId, roles))
                : EmptyPermissions;
        }
        catch (Exception)
        {
            // The grant store may be unavailable (schema not yet created, store down,
            // test database not initialised) — fail soft to the claim-based set rather
            // than turning every permission check into a 500.
            result = EmptyPermissions;
        }

        _resolved = result;
        return result;
    }

    private static readonly IReadOnlySet<string> EmptyPermissions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<string> ClaimPermissions()
        => httpContextAccessor.HttpContext?.User?.Claims
            .Where(c => c.Type == ClaimTypes.Permission)
            .Select(c => c.Value)
            .ToList() ?? [];
}
