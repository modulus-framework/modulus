using System.Security.Claims;
using System.Text.Json;
using TradeFlow.Shared.Application.Authorization;
using TradeFlow.Shared.Application.Abstractions;
using TradeFlow.Shared.Application.Caching;
using TradeFlow.Shared.Domain;
using Microsoft.AspNetCore.Authentication;
using ApplicationException = TradeFlow.Shared.Application.Exceptions.ApplicationException;
using ClaimTypes = System.Security.Claims.ClaimTypes;

namespace TradeFlow.Shared.Infrastructure.Authentication;

public sealed class ClaimsTransformation(
    IServiceScopeFactory serviceScopeFactory,
    ICacheService cache,
    IUserIdentifierMapper userIdentifierMapper)
    : IClaimsTransformation
{
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
    private readonly ICacheService _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly IUserIdentifierMapper _userIdentifierMapper = userIdentifierMapper ?? throw new ArgumentNullException(nameof(userIdentifierMapper));

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (!IsKeycloakToken(principal) || principal.HasClaim(c => c.Type == CustomClaimTypes.UserId))
        {
            return principal;
        }

        using IServiceScope scope = _serviceScopeFactory.CreateScope();
        IPermissionService permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        string? keycloakUserId = principal.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(keycloakUserId))
        {
            keycloakUserId = principal.FindFirst("user_id")?.Value
                           ?? principal.FindFirst("id")?.Value
                           ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                           ?? principal.FindFirst("preferred_username")?.Value
                           ?? principal.FindFirst("email")?.Value
                           ?? principal.FindFirst("name")?.Value;

            if (string.IsNullOrEmpty(keycloakUserId))
            {
                foreach (Claim claim in principal.Claims)
                {
                    if (Guid.TryParse(claim.Value, out Guid guidValue))
                    {
                        keycloakUserId = claim.Value;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(keycloakUserId))
            {
                throw new ApplicationException(
                    "ClaimsTransformation.KeycloakUserIdNotFound",
                    "Keycloak token is missing user identifier claims");
            }
        }

        Result<Guid> userIdResult = await GetApplicationUserIdFromKeycloakIdCached(keycloakUserId);
        if (userIdResult.IsFailure)
        {
            return principal;
        }

        Result<PermissionsResponse> permissionsResult =
            await permissionService.GetUserPermissionsAsync(userIdResult.Value.ToString());
        if (permissionsResult.IsFailure)
        {
            throw new ApplicationException(
                "ClaimsTransformation.PermissionsFailed",
                permissionsResult.Error.Message,
                permissionsResult.Error);
        }

        var claimsIdentity = new ClaimsIdentity();

        claimsIdentity.AddClaim(new Claim(CustomClaimTypes.UserId, permissionsResult.Value.UserId.ToString()));
        claimsIdentity.AddClaim(new Claim("sub", permissionsResult.Value.UserId.ToString()));
        claimsIdentity.AddClaim(new Claim("keycloak_sub", keycloakUserId));

        var addedPermissions = new HashSet<string>();
        foreach (string permission in permissionsResult.Value.Permissions)
        {
            if (addedPermissions.Contains(permission))
            {
                continue;
            }
            claimsIdentity.AddClaim(new Claim(CustomClaimTypes.Permission, permission));
            addedPermissions.Add(permission);
        }

        MapRoles(principal, claimsIdentity);

        string? emailClaim = principal.FindFirst(ClaimTypes.Email)?.Value;
        if (!string.IsNullOrEmpty(emailClaim))
        {
            claimsIdentity.AddClaim(new Claim(ClaimTypes.Email, emailClaim));
        }

        string? nameClaim = principal.FindFirst("name")?.Value ?? principal.FindFirst("preferred_username")?.Value;
        if (!string.IsNullOrEmpty(nameClaim))
        {
            claimsIdentity.AddClaim(new Claim(ClaimTypes.Name, nameClaim));
        }

        principal.AddIdentity(claimsIdentity);
        return principal;
    }

    private static void MapRoles(ClaimsPrincipal principal, ClaimsIdentity claimsIdentity)
    {
        string? realmAccessClaim = principal.FindFirst("realm_access")?.Value;
        if (!string.IsNullOrEmpty(realmAccessClaim))
        {
            try
            {
                using var document = JsonDocument.Parse(realmAccessClaim);
                if (document.RootElement.TryGetProperty("roles", out JsonElement rolesElement))
                {
                    foreach (JsonElement roleElement in rolesElement.EnumerateArray())
                    {
                        string? role = roleElement.GetString();
                        if (!string.IsNullOrEmpty(role))
                        {
                            claimsIdentity.AddClaim(new Claim(ClaimTypes.Role, role));
                            claimsIdentity.AddClaim(new Claim("realm_role", role));
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Malformed realm_access claim — skip role mapping for this section
            }
        }

        string? resourceAccessClaim = principal.FindFirst("resource_access")?.Value;
        if (!string.IsNullOrEmpty(resourceAccessClaim))
        {
            try
            {
                using var document = JsonDocument.Parse(resourceAccessClaim);
                foreach (JsonProperty resourceProperty in document.RootElement.EnumerateObject())
                {
                    string resourceName = resourceProperty.Name;
                    if (resourceProperty.Value.TryGetProperty("roles", out JsonElement rolesElement))
                    {
                        foreach (JsonElement roleElement in rolesElement.EnumerateArray())
                        {
                            string? role = roleElement.GetString();
                            if (!string.IsNullOrEmpty(role))
                            {
                                claimsIdentity.AddClaim(new Claim("client_role", role));
                                claimsIdentity.AddClaim(new Claim($"client_role_{resourceName}", role));
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Malformed resource_access claim — skip client role mapping
            }
        }
    }

    private static bool IsKeycloakToken(ClaimsPrincipal principal)
    {
        return principal.HasClaim(c => c.Type == "iss" && c.Value?.Contains("/realms/") == true) ||
               principal.HasClaim(c => c.Type == "azp");
    }

    private async Task<Result<Guid>> GetApplicationUserIdFromKeycloakIdCached(string keycloakUserId)
    {
        string cacheKey = $"user_mapping:{keycloakUserId}";

        string? cachedUserId = await _cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(cachedUserId) && Guid.TryParse(cachedUserId, out Guid parsedUserId))
        {
            return Result.Success(parsedUserId);
        }

        Result<Guid> result = await _userIdentifierMapper.GetApplicationUserIdFromExternalIdAsync(keycloakUserId, "Keycloak");

        if (result.IsSuccess)
        {
            await _cache.SetStringAsync(cacheKey, result.Value.ToString(), TimeSpan.FromMinutes(15));
        }

        return result;
    }

}
