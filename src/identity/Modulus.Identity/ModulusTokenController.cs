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

/// <summary>
/// Minimal OpenIddict token endpoint supporting the password, refresh and authorization-code flows.
/// The password grant is credential-checked via
/// <see cref="IPasswordGrantCredentialValidator"/>; if no validator is wired
/// the deny-default <see cref="NullPasswordGrantCredentialValidator"/> rejects
/// every request, so tokens are never minted without a real credential check.
/// Override or extend for custom grant types.
/// </summary>
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

        return BadRequest(new { error = "unsupported_grant_type" });
    }

    private async Task<IActionResult> HandlePasswordGrantAsync(OpenIddictRequest request)
    {
        var result = await credentialValidator.ValidateAsync(
            request.Username!, request.Password!, HttpContext.RequestAborted);

        if (!result.Success)
        {
            var properties = new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                    "The username or password is incorrect.",
            });

            return Forbid(properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var identity = new ClaimsIdentity(
            authenticationType: "OpenIddict",
            nameType: OpenIddictConstants.Claims.Name,
            roleType: OpenIddictConstants.Claims.Role);

        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, result.Subject!));
        if (!string.IsNullOrWhiteSpace(result.UserName))
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Name, result.UserName));
        if (!string.IsNullOrWhiteSpace(result.Email))
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Email, result.Email));
        foreach (var role in result.Roles)
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Role, role));

        // Embed the security stamp so the refresh handler can detect
        // password changes / security invalidations without a DB round-trip
        // on every access-token use. The claim is only in the access token
        // (not the identity token) and is validated on refresh.
        if (!string.IsNullOrWhiteSpace(result.SecurityStamp))
            identity.AddClaim(new Claim("security_stamp", result.SecurityStamp));

        var principal = new ClaimsPrincipal(identity);

        // Grant only scopes that are both requested and explicitly allowed.
        var scopes = PasswordGrant.AuthorizeScopes(request.GetScopes(), AllowedGrantScopes);
        principal.SetScopes(scopes);

        ApplyDestinations(principal);

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<IActionResult> HandleRefreshTokenGrantAsync()
    {
        var info = await HttpContext.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        if (!info.Succeeded || info.Principal is null)
            return InvalidRefreshGrant();

        // Resolve the UserManager for the concrete user type registered by
        // AddModulusIdentity<TUser>.  GetService<UserManager<ModulusUser>>
        // returns null for derived types (e.g. AppUser : ModulusUser), which
        // caused the controller to silently skip ALL security checks
        // (IsActive, roles, security stamp, lockout).
        var rawUserManager = ResolveUserManagerRaw();
        var userManager = rawUserManager as UserManager<ModulusUser>;

        ClaimsPrincipal principal;

        if (userManager is not null)
        {
            // Re-verify the subject is still present and active before
            // re-issuing tokens. A refresh token can outlive a disabled/
            // deleted account, so we must not blindly mint a new access token
            // for a stale subject.
            var subject = info.Principal.GetClaim(OpenIddictConstants.Claims.Subject);
            var user = string.IsNullOrWhiteSpace(subject)
                ? null
                : await userManager.FindByIdAsync(subject);

            if (user is not { IsActive: true })
                return InvalidRefreshGrant();

            // Lock-out check: a locked-out user must not be able to refresh.
            // This mirrors the sign-in flow's lock-out policy.
            if (await userManager.IsLockedOutAsync(user))
                return InvalidRefreshGrant();

            // Security-stamp check: if the stamp stored in the refresh token
            // differs from the user's current stamp, the user changed their
            // password or was otherwise security-invalidated. Reject the
            // refresh so the user must re-authenticate. This mirrors
            // SignInManager's ValidateSecurityStampAsync for opaque tokens.
            var storedStamp = info.Principal.FindFirstValue("security_stamp");
            var currentStamp = await userManager.GetSecurityStampAsync(user);
            if (!string.IsNullOrWhiteSpace(storedStamp) &&
                !string.Equals(storedStamp, currentStamp, StringComparison.Ordinal))
            {
                return InvalidRefreshGrant();
            }

            // Rebuild the dynamic claims (name/email/roles) from the CURRENT
            // store state: minting from the refresh principal's frozen claims
            // would keep a role change, demotion, or profile edit invisible
            // until the user logs in again. The subject travels with the
            // refresh token itself, so only volatile claims are refreshed.
            principal = await BuildPrincipalAsync(user, rawUserManager!);
            principal.SetScopes(info.Principal.GetScopes());
        }
        else
        {
            // No Identity user store configured for ModulusUser — nothing to
            // re-verify or refresh beyond the (already server-validated)
            // refresh token; honour its claims as-is.
            principal = info.Principal;
        }

        ApplyDestinations(principal);

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Builds a fresh ClaimsPrincipal from the current store state. Uses
    /// reflection to invoke UserManager methods on the concrete user type
    /// so derived <c>TUser</c> types are handled correctly — a
    /// <c>UserManager&lt;DerivedUser&gt;</c> is NOT covariant with
    /// <c>UserManager&lt;ModulusUser&gt;</c>, so casting fails at runtime.
    /// </summary>
    private static async Task<ClaimsPrincipal> BuildPrincipalAsync(
        ModulusUser user, object userManager)
    {
        var userType = userManager.GetType().GetGenericArguments()[0];
        var userManagerType = typeof(UserManager<>).MakeGenericType(userType);

        var identity = new ClaimsIdentity(
            authenticationType: "OpenIddict",
            nameType: OpenIddictConstants.Claims.Name,
            roleType: OpenIddictConstants.Claims.Role);

        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, user.Id.ToString()));
        if (!string.IsNullOrWhiteSpace(user.UserName))
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Name, user.UserName));
        if (!string.IsNullOrWhiteSpace(user.Email))
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Email, user.Email));

        var getRoles = userManagerType.GetMethod(nameof(UserManager<ModulusUser>.GetRolesAsync))
            ?? throw new InvalidOperationException("GetRolesAsync method not found on UserManager.");
        // UserManager<T>.GetRolesAsync returns Task<IList<string>>; Task<T> is
        // invariant, so the await must be typed Task<IList<string>> — a cast to
        // Task<IReadOnlyList<string>> would throw InvalidCastException on every
        // refresh grant. IList<string> IS assignable to IReadOnlyList<string>.
        var roles = (IReadOnlyList<string>)await (Task<IList<string>>)getRoles.Invoke(userManager, [user])!;

        foreach (var role in roles)
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Role, role));

        var getStamp = userManagerType.GetMethod(nameof(UserManager<ModulusUser>.GetSecurityStampAsync))
            ?? throw new InvalidOperationException("GetSecurityStampAsync method not found on UserManager.");
        var stamp = (string?)await (Task<string>)getStamp.Invoke(userManager, [user])!;

        if (!string.IsNullOrWhiteSpace(stamp))
            identity.AddClaim(new Claim("security_stamp", stamp));

        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Resolves <c>UserManager&lt;TConcreteUser&gt;</c> from DI using the
    /// actual user type registered by <c>AddModulusIdentity&lt;TUser&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The previous code resolved <c>UserManager&lt;ModulusUser&gt;</c>, which
    /// returns <c>null</c> when a derived <c>TUser</c> is registered — causing
    /// the refresh handler to silently skip the IsActive check, role/claim
    /// rebuild, security-stamp verification, and lock-out validation.  This
    /// method resolves the correct concrete <c>UserManager&lt;T&gt;</c> using
    /// the per-host <see cref="ModulusUserTypeDescriptor"/> at runtime.
    /// </remarks>
    private UserManager<ModulusUser>? ResolveUserManager()
        => ResolveUserManagerRaw() as UserManager<ModulusUser>;

    /// <summary>
    /// Resolves the raw <c>UserManager&lt;TConcreteUser&gt;</c> object from
    /// DI without casting — used by <see cref="BuildPrincipalAsync"/> which
    /// invokes methods via reflection (to support derived user types where
    /// the concrete <c>UserManager&lt;DerivedUser&gt;</c> is NOT covariant
    /// with <c>UserManager&lt;ModulusUser&gt;</c>).
    /// </summary>
    private object? ResolveUserManagerRaw()
    {
        // The descriptor is optional so the controller stays directly
        // constructible (tests, custom wiring); without it the base
        // ModulusUser type is used.
        var userType = userTypeDescriptor?.UserType ?? typeof(ModulusUser);
        var userManagerType = typeof(UserManager<>).MakeGenericType(userType);
        return HttpContext.RequestServices.GetService(userManagerType);
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
public class ModulusEndSessionController(
    IOptions<ModulusIdentityOptions> identityOptions) : ControllerBase
{
    [HttpGet("~/connect/end-session")]
    [HttpPost("~/connect/end-session")]
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
