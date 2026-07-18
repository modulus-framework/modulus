namespace Modulus.Testing;

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;

/// <summary>
/// Well-known names for the test authentication scheme and the request headers
/// <see cref="ModulusWebAppFactory{TEntryPoint}.CreateAuthenticatedClient"/> uses
/// to describe the principal. Every request that carries a user header is
/// authenticated by <see cref="TestAuthHandler"/>; requests without one stay
/// anonymous, so a single factory can issue both authenticated and anonymous
/// clients.
/// </summary>
public static class TestAuthDefaults
{
    /// <summary>Name of the authentication scheme the handler registers.</summary>
    public const string SchemeName = "Test";

    /// <summary>Header carrying the user id (mapped to <see cref="ClaimTypes.NameIdentifier"/>).</summary>
    public const string UserIdHeader = "X-Test-UserId";

    /// <summary>Header carrying the user name (mapped to <see cref="ClaimTypes.Name"/>).</summary>
    public const string UserNameHeader = "X-Test-UserName";

    /// <summary>Header carrying the email (mapped to <see cref="ClaimTypes.Email"/>).</summary>
    public const string EmailHeader = "X-Test-Email";

    /// <summary>Header carrying comma-separated roles (each mapped to <see cref="ClaimTypes.Role"/>).</summary>
    public const string RolesHeader = "X-Test-Roles";

    /// <summary>Header carrying comma-separated permissions (each mapped to a <c>permission</c> claim).</summary>
    public const string PermissionsHeader = "X-Test-Permissions";
}

/// <summary>
/// Authenticates requests from their <c>X-Test-*</c> headers without OpenIddict or
/// a real token, so tests exercise <c>[Authorize]</c> endpoints and any
/// <c>ClaimsPrincipal</c>-based <c>ICurrentUser</c> with a caller-chosen identity.
/// A request with no user headers produces <see cref="AuthenticateResult.NoResult"/>
/// (anonymous) rather than a failure, so the same scheme serves anonymous clients.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headers = Request.Headers;
        if (!headers.ContainsKey(TestAuthDefaults.UserIdHeader)
            && !headers.ContainsKey(TestAuthDefaults.UserNameHeader))
        {
            // No identity described → leave the request anonymous.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>();
        Add(claims, ClaimTypes.NameIdentifier, TestAuthDefaults.UserIdHeader);
        Add(claims, ClaimTypes.Name, TestAuthDefaults.UserNameHeader);
        Add(claims, ClaimTypes.Email, TestAuthDefaults.EmailHeader);
        AddMany(claims, ClaimTypes.Role, TestAuthDefaults.RolesHeader);
        AddMany(claims, "permission", TestAuthDefaults.PermissionsHeader);

        var identity = new ClaimsIdentity(
            claims, TestAuthDefaults.SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity), TestAuthDefaults.SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private void Add(List<Claim> claims, string claimType, string header)
    {
        var value = Request.Headers[header].FirstOrDefault();
        if (!string.IsNullOrEmpty(value))
            claims.Add(new Claim(claimType, value));
    }

    private void AddMany(List<Claim> claims, string claimType, string header)
    {
        var value = Request.Headers[header].FirstOrDefault();
        if (string.IsNullOrEmpty(value))
            return;

        foreach (var item in value.Split(',',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            claims.Add(new Claim(claimType, item));
    }
}
