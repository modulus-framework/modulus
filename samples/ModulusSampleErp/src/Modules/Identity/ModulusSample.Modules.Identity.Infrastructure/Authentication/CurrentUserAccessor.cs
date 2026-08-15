using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using ModulusSample.Modules.Identity.Domain.Constants;
using ModulusSample.Shared.Application.Authorization;
using ModulusSample.Shared.Domain.ValueObjects;

namespace ModulusSample.Modules.Identity.Infrastructure.Authentication;

/// <summary>
/// Resolves <see cref="ICurrentUser"/> from the request principal. Permissions
/// are resolved server-side from the identity grant store (the <c>permissions</c>
/// / <c>role_permissions</c> tables) rather than from claims on the token, so a
/// permission change takes effect without re-issuing tokens. Resolution happens
/// at most once per request (the accessor is scoped).
/// </summary>
internal sealed class CurrentUserAccessor(
    IHttpContextAccessor httpContextAccessor,
    IServiceProvider serviceProvider,
    ILogger<CurrentUserAccessor> logger) : ICurrentUser
{
    private IReadOnlySet<string>? _permissions;

    public Guid? UserId
    {
        get
        {
            string? userIdClaim = httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.UserId)?.Value
                ?? httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value
                ?? httpContextAccessor.HttpContext?.User?.FindFirst(
                    System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(userIdClaim, out Guid userId) ? userId : null;
        }
    }

    public string? UserName => httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.UserName)?.Value;
    public string? Email => httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Email)?.Value;
    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
    public bool IsInRole(string role) => httpContextAccessor.HttpContext?.User?.IsInRole(role) ?? false;

    public bool HasPermission(string permission) => ResolvePermissions().Contains(permission);

    public IReadOnlyList<string> Permissions => ResolvePermissions().ToList();

    private IReadOnlySet<string> ResolvePermissions()
    {
        if (_permissions is not null)
        {
            return _permissions;
        }

        var userId = UserId;
        if (userId is null || !IsAuthenticated)
        {
            return _permissions = new HashSet<string>();
        }

        var permissionService = serviceProvider.GetRequiredService<IPermissionService>();
        var result = permissionService
            .GetUserPermissionsAsync(userId.Value.ToString())
            .GetAwaiter()
            .GetResult();

        if (result.IsFailure)
        {
            logger.LogWarning(
                "Failed to resolve permissions for user {UserId}: {Error}",
                userId.Value, result.Error);
            return _permissions = new HashSet<string>();
        }

        return _permissions = result.Value.Permissions;
    }
}
