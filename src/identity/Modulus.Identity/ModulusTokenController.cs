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

        // Re-verify the subject is still present and active before re-issuing
        // tokens. A refresh token can outlive a disabled/deleted account, so
        // we must not blindly mint a new access token for a stale subject.
        if (!await IsSubjectActiveAsync(info.Principal, HttpContext.RequestAborted))
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

        foreach (var claim in info.Principal.Claims)
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

        return SignIn(info.Principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Resolves the current subject from the authenticated refresh-token
    /// principal (<paramref name="principal"/> — NOT the controller's
    /// <c>User</c> property, which is populated by the default authentication
    /// scheme (e.g. the Identity cookie) and is typically anonymous at
    /// <c>/connect/token</c>) and confirms the underlying account still exists
    /// and is active. When ASP.NET Core Identity (with
    /// <see cref="ModulusUser"/>) is wired up, the user store is consulted;
    /// otherwise the presence of a subject claim is treated as sufficient.
    /// </summary>
    private async Task<bool> IsSubjectActiveAsync(
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var subject = principal.GetClaim(OpenIddictConstants.Claims.Subject);
        if (string.IsNullOrWhiteSpace(subject))
            return false;

        var userManager = HttpContext.RequestServices
            .GetService<UserManager<ModulusUser>>();

        // No Identity user store configured for ModulusUser — nothing to
        // re-verify, so honour the (already-validated) refresh token.
        if (userManager is null)
            return true;

        var user = await userManager.FindByIdAsync(subject);
        return user is { IsActive: true };
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
