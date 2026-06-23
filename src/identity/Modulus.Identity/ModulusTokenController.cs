using System.Security.Claims;

namespace Modulus.Identity;

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

/// <summary>
/// Minimal OpenIddict token endpoint supporting password and refresh flows.
/// Override or extend for custom grant types.
/// </summary>
public class ModulusTokenController : Controller
{
    [HttpPost("~/connect/token")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException(
                "Unable to retrieve OpenIddict request.");

        if (request.IsPasswordGrantType())
            return HandlePasswordGrantAsync(request);

        if (request.IsRefreshTokenGrantType())
            return await HandleRefreshTokenGrantAsync();

        return BadRequest(new { error = "unsupported_grant_type" });
    }

    private IActionResult HandlePasswordGrantAsync(OpenIddictRequest request)
    {
        var identity = new ClaimsIdentity(
            authenticationType: "OpenIddict",
            nameType: OpenIddictConstants.Claims.Name,
            roleType: OpenIddictConstants.Claims.Role);

        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, request.Username!));
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Name, request.Username!));

        var principal = new ClaimsPrincipal(identity);

        var scopes = request.GetScopes();
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
            return Forbid();

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
            sub   = User.GetClaim(OpenIddictConstants.Claims.Subject),
            name  = User.GetClaim(OpenIddictConstants.Claims.Name),
            email = User.GetClaim(OpenIddictConstants.Claims.Email),
            roles = User.GetClaims(OpenIddictConstants.Claims.Role)
                        .ToArray(),
        });
    }
}
