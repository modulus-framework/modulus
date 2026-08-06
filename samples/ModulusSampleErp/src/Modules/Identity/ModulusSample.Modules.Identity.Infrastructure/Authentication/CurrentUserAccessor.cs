using Microsoft.AspNetCore.Http;
using Modulus.Core.Abstractions;
using ModulusSample.Modules.Identity.Domain.Constants;
using ModulusSample.Shared.Domain.ValueObjects;

namespace ModulusSample.Modules.Identity.Infrastructure.Authentication;

internal sealed class CurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            string? userIdClaim = httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.UserId)?.Value;
            return Guid.TryParse(userIdClaim, out Guid userId) ? userId : null;
        }
    }

    public string? UserName => httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.UserName)?.Value;
    public string? Email => httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Email)?.Value;
    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
    public bool IsInRole(string role) => httpContextAccessor.HttpContext?.User?.IsInRole(role) ?? false;
    public bool HasPermission(string permission) =>
        httpContextAccessor.HttpContext?.User?.Claims.Any(c => c.Type == ClaimTypes.Permission && c.Value == permission) ?? false;

    public IReadOnlyList<string> Permissions =>
        httpContextAccessor.HttpContext?.User?.Claims
            .Where(c => c.Type == ClaimTypes.Permission)
            .Select(c => c.Value)
            .ToList() ?? [];
}
