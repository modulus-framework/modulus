using ModulusSample.Modules.Identity.Application.Abstractions.Authentication;
using ModulusSample.Modules.Identity.Domain.Constants;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;

namespace ModulusSample.Modules.Identity.Infrastructure.Authentication;

public sealed class UserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
    public Guid UserId
    {
        get
        {
            string? userIdClaim = httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.UserId)?.Value;
            return Guid.TryParse(userIdClaim, out Guid userId) ? userId : Guid.Empty;
        }
    }

    public string? Email => httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Email)?.Value;
    public string? UserName => httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.UserName)?.Value;
    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
    public bool IsInRole(string role) => httpContextAccessor.HttpContext?.User?.IsInRole(role) ?? false;
    public bool HasPermission(string permission) =>
        httpContextAccessor.HttpContext?.User?.Claims.Any(c => c.Type == ClaimTypes.Permission && c.Value == permission) ?? false;
    public IEnumerable<string> GetRoles() =>
        httpContextAccessor.HttpContext?.User?.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value) ?? Enumerable.Empty<string>();
    public IEnumerable<string> GetPermissions() =>
        httpContextAccessor.HttpContext?.User?.Claims.Where(c => c.Type == ClaimTypes.Permission).Select(c => c.Value) ?? Enumerable.Empty<string>();

    /// <summary>
    /// Gets the current JWT access token from the Authorization header
    /// </summary>
    public string? AccessToken
    {
        get
        {
            string? authorizationHeader = httpContextAccessor.HttpContext?.Request?.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(authorizationHeader) || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return authorizationHeader["Bearer ".Length..];
        }
    }

    /// <summary>
    /// Gets the session ID from the 'sid' claim in the JWT
    /// </summary>
    public Guid? SessionId
    {
        get
        {
            string? sessionIdClaim = httpContextAccessor.HttpContext?.User?.FindFirst("sid")?.Value;
            return Guid.TryParse(sessionIdClaim, out Guid sessionId) ? sessionId : null;
        }
    }

    /// <summary>
    /// Gets the external session identifier from the 'sid' claim in the JWT.
    /// This is used to look up the local database session record.
    /// </summary>
    public string? ExternalSessionId =>
        httpContextAccessor.HttpContext?.User?.FindFirst("sid")?.Value;
}
