using System.Security.Claims;

namespace Modulus.Identity;

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Modulus.Identity.Abstractions;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

/// <summary>
/// The authorization endpoint (<c>/connect/authorize</c>) of the authorization-code flow with PKCE: the way a mobile,
/// desktop or single-page client signs a user in without ever seeing the password. It sends the user's browser here;
/// the user signs in through the app's own login page (the ASP.NET Core Identity cookie), and the server answers with
/// a one-time code that the client redeems at <c>/connect/token</c> (see <see cref="ModulusTokenController"/>).
/// </summary>
/// <remarks>
/// <para>
/// Enabled by <c>Identity:AllowAuthorizationCodeFlow</c>, which also makes PKCE mandatory (<c>AddModulusOpenIddict</c>).
/// A client must be registered with the authorization endpoint, the code grant and its exact redirect URIs before this
/// endpoint will talk to it; OpenIddict rejects anything else before this action runs.
/// </para>
/// <para>
/// Consent is implicit: every registered client is trusted with the scopes it may request, which is right for the
/// first-party apps an app owns. A third-party consent screen is a different product and is not provided.
/// </para>
/// <para>
/// The user is signed in with the Identity cookie, so the app needs a login page at the cookie's login path
/// (<c>/account/login</c>, the Identity UI's page) that returns to <c>ReturnUrl</c> after signing in.
/// </para>
/// </remarks>
public class ModulusAuthorizeController : Controller
{
    /// <summary>The claim ASP.NET Core Identity puts the security stamp in.</summary>
    private const string CookieSecurityStampClaim = "AspNet.Identity.SecurityStamp";

    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("Unable to retrieve OpenIddict request.");

        var signedIn = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var needsLogin = !signedIn.Succeeded
            || signedIn.Principal is null
            || request.HasPromptValue(OpenIddictConstants.PromptValues.Login)
            || MaxAgeExceeded(request, signedIn);

        if (needsLogin)
        {
            // prompt=none: the client asked not to show anything, so it can only be told there is no session.
            if (request.HasPromptValue(OpenIddictConstants.PromptValues.None))
                return Rejected(OpenIddictConstants.Errors.LoginRequired, "The user is not signed in.");

            return Challenge(
                new AuthenticationProperties { RedirectUri = ReturnUrl(Request) },
                IdentityConstants.ApplicationScheme);
        }

        var principal = BuildPrincipal(signedIn.Principal!, request.GetScopes());
        if (principal is null)
            return Rejected(OpenIddictConstants.Errors.AccessDenied, "The signed-in user could not be identified.");

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// The principal the authorization code (and the tokens it is later exchanged for) is issued for, built from the
    /// signed-in user's Identity cookie. Only scopes both requested and allowed are granted. Null when the cookie
    /// carries no user id.
    /// </summary>
    internal static ClaimsPrincipal? BuildPrincipal(ClaimsPrincipal user, IEnumerable<string> requestedScopes)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(requestedScopes);

        var subject = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(subject))
            return null;

        var identity = new ClaimsIdentity(
            authenticationType: "OpenIddict",
            nameType: OpenIddictConstants.Claims.Name,
            roleType: OpenIddictConstants.Claims.Role);

        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, subject));
        if (user.FindFirstValue(ClaimTypes.Name) is { Length: > 0 } name)
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Name, name));
        if (user.FindFirstValue(ClaimTypes.Email) is { Length: > 0 } email)
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Email, email));
        foreach (var role in user.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.Ordinal))
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Role, role));

        // Lets the refresh handler notice a password change or another security invalidation (see ModulusTokenController).
        if (user.FindFirstValue(CookieSecurityStampClaim) is { Length: > 0 } stamp)
            identity.AddClaim(new Claim("security_stamp", stamp));

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(PasswordGrant.AuthorizeScopes(requestedScopes, ModulusTokenController.AllowedGrantScopes));
        ModulusTokenController.ApplyDestinations(principal);
        return principal;
    }

    /// <summary>
    /// Where the login page sends the user back to: this same request, minus <c>prompt=login</c> (kept, it would ask for a
    /// second login in a loop). Local by construction, as the login page only follows local return URLs.
    /// </summary>
    internal static string ReturnUrl(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        IEnumerable<KeyValuePair<string, StringValues>> parameters = request.HasFormContentType
            ? request.Form
            : request.Query;

        var kept = new List<KeyValuePair<string, StringValues>>();
        foreach (var (key, value) in parameters)
        {
            if (string.Equals(key, OpenIddictConstants.Parameters.Prompt, StringComparison.Ordinal))
            {
                var prompts = value.ToString()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(p => !string.Equals(p, OpenIddictConstants.PromptValues.Login, StringComparison.Ordinal))
                    .ToArray();
                if (prompts.Length > 0)
                    kept.Add(new(key, string.Join(' ', prompts)));
                continue;
            }

            kept.Add(new(key, value));
        }

        return request.PathBase + request.Path + QueryString.Create(kept);
    }

    /// <summary>OIDC <c>max_age</c>: a session older than the client accepts has to sign in again.</summary>
    private static bool MaxAgeExceeded(OpenIddictRequest request, AuthenticateResult signedIn)
    {
        if (request.MaxAge is not { } maxAge
            || signedIn.Properties?.IssuedUtc is not { } issued)
        {
            return false;
        }

        return DateTimeOffset.UtcNow - issued > TimeSpan.FromSeconds(maxAge);
    }

    private ForbidResult Rejected(string error, string description)
        => Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
            }),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
