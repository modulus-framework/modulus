using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Modulus.Identity;

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Modulus.Identity.Abstractions;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Security;

/// <summary>
/// Minimal OpenIddict token endpoint supporting the password, refresh and authorization-code flows.
/// The password grant is credential-checked via
/// <see cref="IPasswordGrantCredentialValidator"/>; if no validator is wired
/// the deny-default <see cref="NullPasswordGrantCredentialValidator"/> rejects
/// every request, so tokens are never minted without a real credential check.
/// Override or extend for custom grant types.
/// </summary>
[HostTenantContext]
public class ModulusTokenController(
    IPasswordGrantCredentialValidator credentialValidator,
    ModulusUserTypeDescriptor? userTypeDescriptor = null)
    : Controller
{
    /// <summary>
    /// Scopes the password grant is allowed to mint, matching those registered
    /// in <c>AddModulusOpenIddict</c>. Used by <see cref="PasswordGrant.AuthorizeScopes"/>
    /// as defence-in-depth so a compromised/forged request cannot widen scopes.
    /// </summary>
    internal static readonly IReadOnlySet<string> AllowedGrantScopes = new HashSet<string>
    {
        OpenIddictConstants.Scopes.OpenId,
        OpenIddictConstants.Scopes.Email,
        OpenIddictConstants.Scopes.Profile,
        OpenIddictConstants.Scopes.Roles,
        OpenIddictConstants.Scopes.OfflineAccess,
        "modulus",
    };

    /// <summary>
    /// Uniform error description for every refresh-grant rejection. Distinct
    /// messages per failure cause (inactive / locked out / stamp changed) leak
    /// account state to anyone holding a (possibly stolen) refresh token.
    /// </summary>
    private const string RefreshFailedDescription =
        "The refresh token is no longer valid.";

    /// <summary>
    /// Where each claim goes: the subject, name and email into both tokens; the security stamp (internal, only needed to
    /// validate a refresh) and everything else into the access token only, never the identity token the client reads.
    /// </summary>
    internal static void ApplyDestinations(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        foreach (var claim in principal.Claims)
        {
            claim.SetDestinations(claim.Type switch
            {
                OpenIddictConstants.Claims.Name or
                OpenIddictConstants.Claims.Subject or
                OpenIddictConstants.Claims.Email
                    => [OpenIddictConstants.Destinations.AccessToken,
                        OpenIddictConstants.Destinations.IdentityToken],
                _ => [OpenIddictConstants.Destinations.AccessToken],
            });
        }
    }

    [HttpPost("~/connect/token")]
    [IgnoreAntiforgeryToken]
    [AllowAnonymous]
    [Loosened("OAuth token endpoint: OpenIddict authenticates the client and the grant", Framework = true)]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException(
                "Unable to retrieve OpenIddict request.");

        if (request.IsPasswordGrantType())
            return await HandlePasswordGrantAsync(request);

        // A redeemed authorization code (issued by ModulusAuthorizeController) is re-verified and re-issued exactly like a
        // refresh token: OpenIddict has already validated the code, and the user must still exist, be active and unchanged.
        if (request.IsRefreshTokenGrantType() || request.IsAuthorizationCodeGrantType())
            return await HandleRefreshTokenGrantAsync();

        if (request.IsClientCredentialsGrantType())
            return await HandleClientCredentialsGrantAsync(request);

        return BadRequest(new { error = "unsupported_grant_type" });
    }

    /// <summary>
    /// Client credentials grant (enabled by <c>Identity:AllowClientCredentialsFlow</c>). OpenIddict has already
    /// authenticated the confidential client and checked its grant and scope permissions; the token represents the
    /// client itself, so its subject is the client id and it carries no user, roles or refresh token.
    /// </summary>
    private async Task<IActionResult> HandleClientCredentialsGrantAsync(OpenIddictRequest request)
    {
        var clientId = request.ClientId!;
        var binding = HttpContext.RequestServices.GetService<IIntegrationClientDirectory>() is { } directory
            ? await directory.FindAsync(clientId, HttpContext.RequestAborted)
            : null;

        // A client with an end date (or one that was disabled, which sets it to now) gets no new tokens after it.
        if (binding is { ValidUntil: { } until } && until <= DateTimeOffset.UtcNow)
        {
            Audit("token.client-credentials", SecurityAuditOutcomes.Denied, clientId, tenantId: null);
            return Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidClient,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The client is no longer allowed to get tokens.",
                }),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var identity = new ClaimsIdentity(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            OpenIddictConstants.Claims.Name,
            OpenIddictConstants.Claims.Role);
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, clientId));
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Name, clientId));

        // The client acts in the company it is bound to (a pinned token cannot select another with a header), and its role is the
        // handle to grant it permissions with: what it may do lives in the grant store and changes without a new token.
        if (binding is not null)
        {
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Role, IntegrationClients.RoleFor(clientId)));
            if (binding.TenantId is { } tenantId)
                identity.AddClaim(new Claim(TokenPrincipalFactory.TenantClaim, tenantId.ToString()));
        }

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(Abstractions.ClientCredentialsGrant.AuthorizeScopes(request.GetScopes(), AllowedGrantScopes));
        ApplyDestinations(principal);
        Audit("token.client-credentials", SecurityAuditOutcomes.Success, clientId, binding?.TenantId);

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<IActionResult> HandlePasswordGrantAsync(OpenIddictRequest request)
    {
        var result = await credentialValidator.ValidateWithSecondFactorAsync(
            request.Username!, request.Password!, request.GetParameter("mfa_code")?.ToString(), HttpContext.RequestAborted);

        if (!result.Success)
        {
            // The validator records why (it knows the account); a custom validator gets this record at least.
            Audit("token.password", SecurityAuditOutcomes.Denied, actor: null, tenantId: null, request.ClientId);
            var properties = new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                    result.Error == PasswordGrantResult.MfaRequiredError
                        ? "A verification code is required: send it as mfa_code."
                        : "The username or password is incorrect.",
            });

            return Forbid(properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var principal = TokenPrincipalFactory.Create(
            result.Subject!,
            result.UserName,
            result.Email,
            result.TenantId,
            result.Roles,
            result.SecurityStamp);

        // Grant only scopes that are both requested and explicitly allowed.
        var scopes = PasswordGrant.AuthorizeScopes(request.GetScopes(), AllowedGrantScopes);
        principal.SetScopes(scopes);

        ApplyDestinations(principal);
        Audit("token.password", SecurityAuditOutcomes.Success, result.Subject, result.TenantId, request.ClientId);

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<IActionResult> HandleRefreshTokenGrantAsync()
    {
        var info = await HttpContext.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        var grant = HttpContext.GetOpenIddictServerRequest()?.IsAuthorizationCodeGrantType() == true ? "token.authorization-code" : "token.refresh";
        if (!info.Succeeded || info.Principal is null)
        {
            Audit(grant, SecurityAuditOutcomes.Denied, actor: null, tenantId: null);
            return InvalidRefreshGrant();
        }

        // Re-verify against the store for the concrete user type registered by AddModulusIdentity<TUser>. The manager
        // is kept untyped: UserManager<AppUser> is not a UserManager<ModulusUser>, so casting it made every derived user
        // type skip the active, lock-out and security-stamp checks and keep its stale claims.
        ClaimsPrincipal principal;
        if (ResolveUserManager() is { } userManager)
        {
            var refreshed = await TokenPrincipalFactory.RevalidateAsync(userManager, info.Principal);
            if (refreshed is null)
            {
                // Inactive, locked out, or the security stamp changed (password reset, sign-out everywhere).
                Audit(grant, SecurityAuditOutcomes.Denied, info.Principal.GetClaim(OpenIddictConstants.Claims.Subject), TenantOf(info.Principal));
                return InvalidRefreshGrant();
            }

            principal = refreshed;
            principal.SetScopes(info.Principal.GetScopes());
        }
        else
        {
            // No Identity user store configured — nothing to re-verify or refresh beyond the (already server-validated)
            // refresh token; honour its claims as-is.
            principal = info.Principal;
        }

        ApplyDestinations(principal);
        Audit(grant, SecurityAuditOutcomes.Success, principal.GetClaim(OpenIddictConstants.Claims.Subject), TenantOf(principal));

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static Guid? TenantOf(ClaimsPrincipal principal)
        => Guid.TryParse(principal.GetClaim("tid"), out var tenantId) ? tenantId : null;

    /// <summary>Records a token decision in the security audit (no user names, passwords or tokens).</summary>
    private void Audit(string action, string outcome, string? actor, Guid? tenantId, string? clientId = null)
        => HttpContext.RequestServices.GetService<ISecurityAuditLog>()?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Identity,
            Action = action,
            Outcome = outcome,
            Actor = actor,
            TenantId = tenantId,
            Details = new Dictionary<string, string?> { ["client"] = clientId ?? HttpContext.GetOpenIddictServerRequest()?.ClientId },
        });

    /// <summary>
    /// Resolves the <c>UserManager&lt;TUser&gt;</c> for the user type registered by <c>AddModulusIdentity&lt;TUser&gt;</c>
    /// (the per-host <see cref="ModulusUserTypeDescriptor"/>; without it the base <see cref="ModulusUser"/>, so the
    /// controller stays directly constructible). Null when no Identity store is registered.
    /// </summary>
    private object? ResolveUserManager()
    {
        var userType = userTypeDescriptor?.UserType ?? typeof(ModulusUser);
        return HttpContext.RequestServices.GetService(typeof(UserManager<>).MakeGenericType(userType));
    }

    private ForbidResult InvalidRefreshGrant()
        => Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] =
                    OpenIddictConstants.Errors.InvalidGrant,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                    RefreshFailedDescription,
            }),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}

/// <summary>
/// Returns claims for authenticated users via the userinfo endpoint.
/// </summary>
public class ModulusUserInfoController : ControllerBase
{
    [HttpGet("~/connect/userinfo")]
    [HttpPost("~/connect/userinfo")]
    [Authorize]
    public IActionResult UserInfo()
    {
        if (User.Identity?.IsAuthenticated != true)
            return Challenge();

        return Ok(new
        {
            sub = User.GetClaim(OpenIddictConstants.Claims.Subject),
            name = User.GetClaim(OpenIddictConstants.Claims.Name),
            email = User.GetClaim(OpenIddictConstants.Claims.Email),
            roles = User.GetClaims(OpenIddictConstants.Claims.Role)
                        .ToArray(),
        });
    }
}

/// <summary>
/// Token introspection endpoint (RFC 7662): validates the <b>submitted</b>
/// token (the <c>token</c> form parameter) against this server's signing and
/// encryption credentials — it does NOT report on the caller's own bearer
/// token. RFC 7662 §2.1 requires the caller (a protected resource) to
/// authenticate: the caller must present the client credentials configured on
/// <see cref="ModulusIdentityOptions.IntrospectionClientId"/>/
/// <see cref="ModulusIdentityOptions.IntrospectionClientSecret"/> via HTTP
/// Basic or the <c>client_id</c>/<c>client_secret</c> form fields. With no
/// credentials configured the endpoint denies everyone (deny-by-default) —
/// an introspection endpoint reachable with any end-user access token would
/// let any token holder profile arbitrary submitted tokens.
/// </summary>
public class ModulusIntrospectionController(
    IOptions<OpenIddictServerOptions> serverOptions,
    IOptions<ModulusIdentityOptions> identityOptions) : ControllerBase
{
    [HttpPost("~/connect/introspect")]
    [IgnoreAntiforgeryToken]
    [AllowAnonymous]
    [Loosened("RFC 7662 introspection: the action authenticates the calling resource server's client credentials", Framework = true)]
    public async Task<IActionResult> Introspect([FromForm] string? token)
    {
        if (!IsCallerAuthorized())
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(token))
            return Ok(new { active = false });

        var result = await ValidateTokenAsync(token);
        if (result is null)
            return Ok(new { active = false });

        var claims = result.Claims.ToList();

        return Ok(new
        {
            active = true,
            sub = First(claims, OpenIddictConstants.Claims.Subject),
            client_id = First(claims, OpenIddictConstants.Claims.ClientId),
            username = First(claims, OpenIddictConstants.Claims.Username),
            scope = string.Join(" ",
                claims.Where(c => c.Type is "scp" or OpenIddictConstants.Claims.Scope)
                      .Select(c => c.Value)),
            token_type = First(claims, OpenIddictConstants.Claims.TokenType) ?? "Bearer",
            exp = AsLong(First(claims, OpenIddictConstants.Claims.ExpiresAt)),
            iat = AsLong(First(claims, OpenIddictConstants.Claims.IssuedAt)),
            nbf = AsLong(First(claims, OpenIddictConstants.Claims.NotBefore)),
            jti = First(claims, "jti"),
            iss = First(claims, OpenIddictConstants.Claims.Issuer),
            aud = string.Join(" ",
                claims.Where(c => c.Type == OpenIddictConstants.Claims.Audience)
                      .Select(c => c.Value)),
        });
    }

    /// <summary>
    /// RFC 7662 caller authentication: the configured client credentials must
    /// be presented — HTTP Basic (the RFC 7662 convention) or the
    /// <c>client_id</c>/<c>client_secret</c> form fields (OAuth 2.0 client
    /// authentication). A bearer end-user token authenticates nothing here.
    /// When no credentials are configured, denies everyone. Comparisons are
    /// constant-time.
    /// </summary>
    private bool IsCallerAuthorized()
    {
        var opts = identityOptions.Value;
        var clientId = opts.IntrospectionClientId;
        var clientSecret = opts.IntrospectionClientSecret;
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            return false;

        var authorization = Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            string decoded;
            try
            {
                decoded = Encoding.UTF8.GetString(
                    Convert.FromBase64String(authorization["Basic ".Length..].Trim()));
            }
            catch (FormatException)
            {
                return false;
            }

            // user-pass colon split — the password may itself contain colons.
            var separator = decoded.IndexOf(':');
            var id = separator < 0 ? decoded : decoded[..separator];
            var secret = separator < 0 ? string.Empty : decoded[(separator + 1)..];
            return Matches(id, clientId) && Matches(secret, clientSecret);
        }

        if (Request.HasFormContentType)
        {
            var formId = Request.Form["client_id"].FirstOrDefault();
            var formSecret = Request.Form["client_secret"].FirstOrDefault();
            if (!string.IsNullOrEmpty(formId))
                return Matches(formId, clientId) && Matches(formSecret, clientSecret);
        }

        return false;
    }

    private static bool Matches(string? actual, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (actual is null)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual),
            Encoding.UTF8.GetBytes(expected));
    }

    /// <summary>
    /// Validates the submitted token with this server's own credentials
    /// (signature, issuer, lifetime; JWE-decrypts when encrypted tokens are
    /// used). Returns the validated principal or <c>null</c> for any
    /// invalid/expired/tampered token.
    /// </summary>
    private async Task<ClaimsIdentity?> ValidateTokenAsync(string token)
    {
        var options = serverOptions.Value;

        // The default issuer matches what OpenIddict stamps on minted tokens:
        // the configured issuer, else the current request's base URL.
        var issuer = options.Issuer?.AbsoluteUri
            ?? $"{Request.Scheme}://{Request.Host.Value}{Request.PathBase.Value}";

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

    private static string? First(
        IEnumerable<Claim> claims, string type)
        => claims.FirstOrDefault(c => c.Type == type)?.Value;

    private static long? AsLong(string? value)
        => long.TryParse(value, out var parsed) ? parsed : null;
}

/// <summary>
/// End session endpoint (RP-initiated logout): signs the user out and
/// optionally redirects to a post-logout URI. The redirect is honored ONLY
/// for URIs on the exact allow-list configured via
/// <c>Identity:AllowedPostLogoutRedirectUris</c> — an unrestricted redirect
/// would be an open-redirect/phishing vector (the attacker logs the victim
/// out and lands them on a lookalike page). When the URI is not allow-listed
/// the endpoint logs out and returns 200 JSON.
/// </summary>
/// <remarks>
/// Revoking issued access/refresh tokens requires the OpenIddict token store
/// (<c>AddModulusIdentityStore</c>) and the <c>/connect/revoke</c> endpoint —
/// this endpoint always terminates the cookie session.
/// </remarks>
[HostTenantContext]
public class ModulusEndSessionController(
    IOptions<ModulusIdentityOptions> identityOptions) : ControllerBase
{
    [HttpGet("~/connect/end-session")]
    [HttpPost("~/connect/end-session")]
    [AllowAnonymous]
    [Loosened("RP-initiated logout must work after the session expired", Framework = true)]
    public async Task<IActionResult> EndSessionAsync(
        [FromQuery] string? post_logout_redirect_uri)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            // Sign out the ambient default schemes (the Identity cookie when
            // AddModulusIdentity is used). Best-effort: bearer-only setups
            // have no sign-out handler, and failing the whole logout for
            // that would turn a benign GET into a 500.
            try
            {
                await HttpContext.SignOutAsync();
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (!string.IsNullOrEmpty(post_logout_redirect_uri)
            && TryResolveAllowedUri(post_logout_redirect_uri, out var canonical))
        {
            // Redirect to the CANONICAL configured URI, never the raw
            // caller-supplied string — the parsed form cannot smuggle
            // userinfo/fragment tricks past the allow-list comparison.
            return Redirect(canonical);
        }

        return Ok(new { message = "Logged out successfully" });
    }

    /// <summary>
    /// Exact-match allow-list check: the caller-supplied URI must be absolute
    /// and identical (scheme/host/port/path/query) to a configured entry.
    /// </summary>
    private bool TryResolveAllowedUri(string candidate, out string canonical)
    {
        canonical = "";

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var requested)
            || string.IsNullOrEmpty(requested.Scheme))
        {
            return false;
        }

        foreach (var configured in identityOptions.Value.AllowedPostLogoutRedirectUris)
        {
            if (Uri.TryCreate(configured, UriKind.Absolute, out var allowed)
                && string.Equals(
                    requested.AbsoluteUri, allowed.AbsoluteUri,
                    StringComparison.Ordinal))
            {
                canonical = allowed.AbsoluteUri;
                return true;
            }
        }

        return false;
    }
}
