using System.Security.Claims;
using ApplicationException = TradeFlow.Shared.Application.Exceptions.ApplicationException;

namespace TradeFlow.Shared.Infrastructure.Authentication;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal? principal)
    {
        string? userId = principal?.FindFirst(CustomClaimTypes.UserId)?.Value;

        return Guid.TryParse(userId, out Guid parsedUserId)
            ? parsedUserId
            : throw new ApplicationException("User identifier is unavailable");
    }

    public static string GetUserName(this ClaimsPrincipal? principal)
    {
        return principal?.FindFirst(ClaimTypes.Name)?.Value ??
               throw new ApplicationException("User name is unavailable");
    }

    public static string GetIdentityId(this ClaimsPrincipal? principal)
    {
        return principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
               throw new ApplicationException("User identity is unavailable");
    }

    public static HashSet<string> GetPermissions(this ClaimsPrincipal? principal)
    {
        IEnumerable<Claim> permissionClaims = principal?.FindAll(CustomClaimTypes.Permission) ??
                                              throw new ApplicationException("Permissions are unavailable");

        return permissionClaims.Select(c => c.Value).ToHashSet();
    }
}
