using System.Security.Claims;

namespace Modulus.Identity;

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Identity.Abstractions;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

/// <summary>
/// Minimal OpenIddict token endpoint supporting password and refresh flows.
/// The password grant is credential-checked via
/// <see cref="IPasswordGrantCredentialValidator"/>; if no validator is wired
/// the deny-default <see cref="NullPasswordGrantCredentialValidator"/> rejects
/// every request, so tokens are never minted without a real credential check.
/// Override or extend for custom grant types.
/// </summary>
public class ModulusTokenController(
    IPasswordGrantCredentialValidator credentialValidator)
    : Controller
{
    /// <summary>
    /// Scopes the password grant is allowed to mint, matching those registered
    /// in <c>AddModulusOpenIddict</c>. Used by <see cref="PasswordGrant.AuthorizeScopes"/>
    /// as defence-in-depth so a compromised/forged request cannot widen scopes.
    /// </summary>
    private static readonly IReadOnlySet<string> AllowedGrantScopes = new HashSet<string>
    {
        OpenIddictConstants.Scopes.OpenId,
        OpenIddictConstants.Scopes.Email,
        OpenIddictConstants.Scopes.Profile,
        OpenIddictConstants.Scopes.Roles,
        OpenIddictConstants.Scopes.OfflineAccess,
        "modulus",
    };

    [HttpPost("~/connect/token")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException(
                "Unable to retrieve OpenIddict request.");

        if (request.IsPasswordGrantType())
            return await HandlePasswordGrantAsync(request);

        if (request.IsRefreshTokenGrantType())
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

        foreach (var claim in principal.Claims)
        {
            claim.SetDestinations(claim.Type switch
            {
                OpenIddictConstants.Claims.Name or
                OpenIddictConstants.Claims.Subject or
                OpenIddictConstants.Claims.Email
                    => [OpenIddictConstants.Destinations.AccessToken,
                        OpenIddictConstants.Destinations.IdentityToken],
                // Security stamp is internal — only needed in the access
                // token for refresh-time validation; never in the identity
                // token which is sent to the client.
                "security_stamp"
                    => [OpenIddictConstants.Destinations.AccessToken],
                _ => [OpenIddictConstants.Destinations.AccessToken],
            });
        }

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<IActionResult> HandleRefreshTokenGrantAsync()
    {
        var info = await HttpContext.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        if (!info.Succeeded || info.Principal is null)
            return Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                        "The refresh token is no longer valid.",
                }),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        var userManager = HttpContext.RequestServices
            .GetService<UserManager<ModulusUser>>();

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
            {
                return Forbid(
                    new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "The user account is no longer active.",
                    }),
                    OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            // Rebuild the dynamic claims (name/email/roles) from the CURRENT
            // store state: minting from the refresh principal's frozen claims
            // would keep a role change, demotion, or profile edit invisible
            // until the user logs in again. The subject travels with the
            // refresh token itself, so only volatile claims are refreshed.
            principal = await BuildPrincipalAsync(user, userManager);
            principal.SetScopes(info.Principal.GetScopes());

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
                return Forbid(
                    new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] =
                            OpenIddictConstants.Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "The security stamp has changed; please sign in again.",
                    }),
                    OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }
        }
        else
        {
            // No Identity user store configured for ModulusUser — nothing to
            // re-verify or refresh beyond the (already server-validated)
            // refresh token; honour its claims as-is.
            principal = info.Principal;
        }

        foreach (var claim in principal.Claims)
        {
            claim.SetDestinations(claim.Type switch
            {
                OpenIddictConstants.Claims.Name or
                OpenIddictConstants.Claims.Subject or
                OpenIddictConstants.Claims.Email
                    => [OpenIddictConstants.Destinations.AccessToken,
                        OpenIddictConstants.Destinations.IdentityToken],
                "security_stamp"
                    => [OpenIddictConstants.Destinations.AccessToken],
                _ => [OpenIddictConstants.Destinations.AccessToken],
            });
        }

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<ClaimsPrincipal> BuildPrincipalAsync(
        ModulusUser user, UserManager<ModulusUser> userManager)
    {
        var identity = new ClaimsIdentity(
            authenticationType: "OpenIddict",
            nameType: OpenIddictConstants.Claims.Name,
            roleType: OpenIddictConstants.Claims.Role);

        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, user.Id.ToString()));
        if (!string.IsNullOrWhiteSpace(user.UserName))
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Name, user.UserName));
        if (!string.IsNullOrWhiteSpace(user.Email))
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Email, user.Email));
        foreach (var role in await userManager.GetRolesAsync(user))
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Role, role));

        // Embed the current security stamp so the next refresh can detect
        // concurrent password changes. Without this, every refresh after
        // the initial login would carry a stale stamp and be rejected.
        var stamp = await userManager.GetSecurityStampAsync(user);
        if (!string.IsNullOrWhiteSpace(stamp))
            identity.AddClaim(new Claim("security_stamp", stamp));

        return new ClaimsPrincipal(identity);
    }
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
