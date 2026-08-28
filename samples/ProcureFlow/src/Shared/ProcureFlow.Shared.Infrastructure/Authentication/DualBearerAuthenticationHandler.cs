using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ProcureFlow.Shared.Infrastructure.Authentication;

internal sealed class DualBearerAuthenticationHandler(
    IOptionsMonitor<DualBearerOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<DualBearerOptions>(options, logger, encoder)
{
    private const string OpenIddictValidationScheme = "OpenIddict.Validation.AspNetCore";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        AuthenticateResult result = await Context.AuthenticateAsync(OpenIddictValidationScheme);

        if (result.Succeeded)
        {
            Logger.LogDebug("Dual auth: authenticated via OpenIddict local validation");
            return result;
        }

        result = await Context.AuthenticateAsync("ExternalOidc");

        if (result.Succeeded)
        {
            Logger.LogDebug("Dual auth: authenticated via external OIDC provider");
            return result;
        }

        Logger.LogWarning("Dual auth: token rejected by both OpenIddict and external OIDC");
        return AuthenticateResult.Fail("Token validation failed with all configured providers");
    }
}

internal sealed class DualBearerOptions : AuthenticationSchemeOptions;
