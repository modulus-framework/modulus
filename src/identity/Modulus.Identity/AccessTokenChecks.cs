using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Modulus.Core.Abstractions.Security;
using Modulus.Identity.Abstractions;
using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace Modulus.Identity;

/// <summary>
/// The access-token checks a resource server and the token endpoint both make: a signed token is valid only while its stored
/// entry is a live access token and its session has not ended. Shared so that introspection and token exchange (spec 12A) cannot
/// drift apart.
/// </summary>
internal static class AccessTokenChecks
{
    /// <summary>
    /// A signed token is reported active only while the checks a resource server makes still hold: its stored entry is an
    /// access token that is still valid (not revoked or expired), and its session has not ended.
    /// </summary>
    public static async Task<bool> IsInForceAsync(HttpContext http, ClaimsIdentity claims)
    {
        // Without a stored entry there is nothing to check revocation against, so the token is not reported active. Id tokens
        // have no entry, and the entry records whether the token is an access token.
        var manager = http.RequestServices.GetService<IOpenIddictTokenManager>();
        if (manager is null || First(claims.Claims, OpenIddictConstants.Claims.Private.TokenId) is not { Length: > 0 } tokenId)
            return false;
        if (await manager.FindByIdAsync(tokenId) is not { } entry
            || !IsAccessTokenEntryType(await manager.GetTypeAsync(entry))
            || await manager.GetStatusAsync(entry) != OpenIddictConstants.Statuses.Valid)
            return false;

        if (First(claims.Claims, TokenPrincipalFactory.SessionClaim) is { Length: > 0 } sid
            && http.RequestServices.GetService<ISessionRegistry>() is { } registry)
        {
            // A sid that is not a session id cannot have been issued by this server.
            var state = Guid.TryParse(sid, out var sessionId)
                ? await registry.TouchAsync(sessionId, ct: http.RequestAborted)
                : SessionState.Unknown;
            if (state != SessionState.Active)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Validates the submitted token with this server's own credentials
    /// (signature, issuer, lifetime; JWE-decrypts when encrypted tokens are
    /// used). Returns the validated principal or <c>null</c> for any
    /// invalid/expired/tampered token.
    /// </summary>
    public static async Task<ClaimsIdentity?> ValidateTokenAsync(HttpContext http, OpenIddictServerOptions options, string token)
    {

        // The default issuer matches what OpenIddict stamps on minted tokens: the configured issuer, else the current request's
        // base URL. OpenIddict writes the base URL with a trailing slash ("https://host/"), so the request form ends with one too.
        var issuer = options.Issuer?.AbsoluteUri
            ?? $"{http.Request.Scheme}://{http.Request.Host.Value}{(http.Request.PathBase.Value ?? "").TrimEnd('/')}/";

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = issuer,
            ValidateAudience = false,
            IssuerSigningKeys = options.SigningCredentials
                .Select(c => c.Key)
                .ToList(),
            TokenDecryptionKeys = options.EncryptionCredentials
                .Select(c => c.Key)
                .ToList(),
            ValidAlgorithms = options.SigningCredentials
                .Select(c => c.Algorithm)
                .Distinct()
                .ToList(),
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };

        try
        {
            var handler = new JsonWebTokenHandler();
            if (!handler.CanReadToken(token))
                return null;

            var result = await handler.ValidateTokenAsync(token, parameters);
            if (!result.IsValid || result.Claims is null)
                return null;

            // IdentityModel 8 exposes the validated claims as a raw
            // dictionary; rebuild a ClaimsIdentity for uniform lookup.
            var identity = new ClaimsIdentity();
            foreach (var (type, value) in result.Claims)
                identity.AddClaim(new Claim(type, value.ToString() ?? string.Empty));

            return identity;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// OpenIddict stores an access token's type as the RFC 8693 URN (<c>urn:ietf:params:oauth:token-type:access_token</c>), not
    /// the short <c>access_token</c> hint, so both spellings are accepted.
    /// </summary>
    internal static bool IsAccessTokenEntryType(string? type)
        => type is OpenIddictConstants.TokenTypeHints.AccessToken or "urn:ietf:params:oauth:token-type:access_token";

    internal static string? First(
        IEnumerable<Claim> claims, string type)
        => claims.FirstOrDefault(c => c.Type == type)?.Value;
}
