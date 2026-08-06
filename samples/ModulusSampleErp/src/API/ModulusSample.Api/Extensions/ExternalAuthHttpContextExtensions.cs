using System.Security.Claims;

namespace ModulusSample.Api.Extensions;

internal static class ExternalAuthHttpContextExtensions
{
    public static string? GetUserId(this ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? user.FindFirst("sub")?.Value
               ?? user.FindFirst("user_id")?.Value;
    }
}
