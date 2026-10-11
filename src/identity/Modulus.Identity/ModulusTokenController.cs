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
using Modulus.Authorization;
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
                OpenIddictConstants.Claims.Email or
                TokenPrincipalFactory.SessionClaim
                    // The session id also goes to the identity token, so an application can match a back-channel logout token's sid.
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
        if (request.IsRefreshTokenGrantType() || request.IsAuthorizationCodeGrantType() || request.IsDeviceCodeGrantType())
            return await HandleRefreshTokenGrantAsync();

        if (request.IsClientCredentialsGrantType())
            return await HandleClientCredentialsGrantAsync(request);

        // Federated token exchange: only registered (and allowed by OpenIddict) when Identity:FederatedLogin:Enabled is set.
        if (request.GrantType == FederatedLoginGrant.GrantType)
            return await HandleFederatedGrantAsync(request);

        // Shop-floor PIN sign-in: only registered (and allowed by OpenIddict) when Identity:ShopFloor:Enabled is set.
        if (request.GrantType == ShopFloorGrant.GrantType)
            return await HandleShopFloorGrantAsync(request);

        // Passkey sign-in: only registered (and allowed by OpenIddict) when Identity:Passkeys:Enabled is set.
        if (request.GrantType == PasskeyGrant.GrantType)
            return await HandlePasskeyGrantAsync(request);

        return BadRequest(new { error = "unsupported_grant_type" });
    }

    /// <summary>
    /// Passkey sign-in (<see cref="PasskeyGrant"/>): the challenge id from <c>POST /account/passkeys/login/options</c> and the
    /// browser's signed assertion. The token records <c>amr</c> <c>hwk</c>/<c>swk</c>, <c>user</c> and <c>mfa</c>, so it also
    /// satisfies step-up authentication.
    /// </summary>
    private async Task<IActionResult> HandlePasskeyGrantAsync(OpenIddictRequest request)
    {
        var passkeys = HttpContext.RequestServices.GetService<IPasskeyService>();
        var assertion = request.GetParameter(PasskeyGrant.AssertionParameter)?.ToString();
        if (passkeys is null
            || !Guid.TryParse(request.GetParameter(PasskeyGrant.ChallengeParameter)?.ToString(), out var challengeId)
            || string.IsNullOrWhiteSpace(assertion))
            return ForbidGrant(OpenIddictConstants.Errors.InvalidRequest, "The passkey challenge or assertion is missing.");

        var result = await passkeys.ValidateLoginAsync(challengeId, assertion, HttpContext.RequestAborted);
        if (!result.Success)
        {
            Audit("token.passkey", SecurityAuditOutcomes.Denied, actor: null, tenantId: null, request.ClientId);
            return ForbidGrant(OpenIddictConstants.Errors.InvalidGrant, "The passkey could not be accepted.");
        }

        var principal = TokenPrincipalFactory.Create(
            result.Subject!,
            result.UserName,
            result.Email,
            result.TenantId,
            result.Roles,
            result.SecurityStamp,
            result.AuthenticationMethods,
            Now);

        principal.SetScopes(PasswordGrant.AuthorizeScopes(request.GetScopes(), AllowedGrantScopes));
        if (await AttachSessionAsync(principal, request) is { } refused)
            return refused;

        ApplyDestinations(principal);
        Audit("token.passkey", SecurityAuditOutcomes.Success, result.Subject, result.TenantId, request.ClientId);

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Shop-floor sign-in (<see cref="ShopFloorGrant"/>): an employee code and PIN from a registered device. The calling
    /// client must be bound to a company (<see cref="IIntegrationClientDirectory"/>) and still valid, and the operator must
    /// belong to that company. The token is short-lived, has no refresh token, and carries only the configured floor roles.
    /// </summary>
    private async Task<IActionResult> HandleShopFloorGrantAsync(OpenIddictRequest request)
    {
        var services = HttpContext.RequestServices;
        var validator = services.GetService<IShopFloorCredentialValidator>();
        var settings = services.GetService<IOptions<ShopFloorOptions>>()?.Value;
        var code = request.GetParameter(ShopFloorGrant.EmployeeCodeParameter)?.ToString();
        var pin = request.GetParameter(ShopFloorGrant.PinParameter)?.ToString();
        if (validator is null || settings is not { Enabled: true } || string.IsNullOrWhiteSpace(code) || string.IsNullOrEmpty(pin))
            return ForbidGrant(OpenIddictConstants.Errors.InvalidRequest, "The employee code and PIN are required.");

        var deviceId = request.ClientId;
        var device = !string.IsNullOrEmpty(deviceId) && services.GetService<IIntegrationClientDirectory>() is { } directory
            ? await directory.FindAsync(deviceId, HttpContext.RequestAborted)
            : null;
        if (device is not { TenantId: { } tenantId } || (device.ValidUntil is { } until && until <= DateTimeOffset.UtcNow))
        {
            Audit("token.shop-floor", SecurityAuditOutcomes.Denied, actor: null, tenantId: null, deviceId);
            return ForbidGrant(OpenIddictConstants.Errors.InvalidClient, "This device is not registered for shop-floor sign-in.");
        }

        var newPin = request.GetParameter(ShopFloorGrant.NewPinParameter)?.ToString();
        var result = await validator.ValidateAsync(tenantId, code, pin, newPin, HttpContext.RequestAborted);
        if (!result.Success)
        {
            Audit("token.shop-floor", SecurityAuditOutcomes.Denied, actor: null, tenantId, deviceId);
            return result.Error switch
            {
                ShopFloorGrant.PinChangeRequiredError => ForbidGrant(ShopFloorGrant.PinChangeRequiredError, "Choose your own PIN: send it as new_pin."),
                ShopFloorGrant.InvalidNewPinError => ForbidGrant(ShopFloorGrant.InvalidNewPinError, "The new PIN is not acceptable (digits only, the allowed length, not one repeated digit, not the old PIN)."),
                _ => ForbidGrant(OpenIddictConstants.Errors.InvalidGrant, "The employee code or PIN is incorrect."),
            };
        }

        var principal = TokenPrincipalFactory.Create(
            result.Subject!,
            result.UserName,
            email: null,
            result.TenantId,
            result.Roles,
            result.SecurityStamp,
            ["pin"],
            Now);

        // No refresh token: offline_access is never granted, and the operator signs in again when the token ends.
        principal.SetScopes(PasswordGrant.AuthorizeScopes(request.GetScopes(), AllowedGrantScopes)
            .Where(s => s != OpenIddictConstants.Scopes.OfflineAccess));
        principal.SetAccessTokenLifetime(TimeSpan.FromMinutes(settings.AccessTokenMinutes));
        if (await AttachSessionAsync(principal, request) is { } refused)
            return refused;

        ApplyDestinations(principal);
        Audit("token.shop-floor", SecurityAuditOutcomes.Success, result.Subject, result.TenantId, deviceId);

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Federated token exchange (<see cref="FederatedLoginGrant"/>): the external provider's token in <c>subject_token</c>
    /// is validated and mapped to a local account by <see cref="IFederatedLoginValidator"/>, then Modulus tokens are issued
    /// exactly as for the password grant. Refused when the exchange is not registered.
    /// </summary>
    private async Task<IActionResult> HandleFederatedGrantAsync(OpenIddictRequest request)
    {
        var validator = HttpContext.RequestServices.GetService<IFederatedLoginValidator>();
        var subjectToken = request.GetParameter(FederatedLoginGrant.SubjectTokenParameter)?.ToString();
        if (validator is null || string.IsNullOrWhiteSpace(subjectToken))
            return ForbidGrant(OpenIddictConstants.Errors.InvalidRequest, "The external sign-in token is missing.");

        var result = await validator.ValidateAsync(subjectToken, HttpContext.RequestAborted);
        if (!result.Success)
        {
            Audit("token.federated", SecurityAuditOutcomes.Denied, actor: null, tenantId: null, request.ClientId);
            return ForbidGrant(OpenIddictConstants.Errors.InvalidGrant, "The external sign-in could not be accepted.");
        }

        var principal = TokenPrincipalFactory.Create(
            result.Subject!,
            result.UserName,
            result.Email,
            result.TenantId,
            result.Roles,
            result.SecurityStamp,
            ["fed"],
            Now);

        principal.SetScopes(PasswordGrant.AuthorizeScopes(request.GetScopes(), AllowedGrantScopes));
        if (await AttachSessionAsync(principal, request) is { } refused)
            return refused;

        ApplyDestinations(principal);
        Audit("token.federated", SecurityAuditOutcomes.Success, result.Subject, result.TenantId, request.ClientId);

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private ForbidResult ForbidGrant(string error, string description)
        => Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
            }),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

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
        // Lets step-up treat this token as a machine (it has no second factor). Only this grant issues the claim.
        identity.AddClaim(new Claim(StepUpPolicy.MachineClaim, "true"));

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
                    result.Error switch
                    {
                        PasswordGrantResult.MfaRequiredError => "A verification code is required: send it as mfa_code.",
                        PasswordGrantResult.PasswordExpiredError => "The password has expired and must be changed.",
                        _ => "The username or password is incorrect.",
                    },
            });

            return Forbid(properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var principal = TokenPrincipalFactory.Create(
            result.Subject!,
            result.UserName,
            result.Email,
            result.TenantId,
            result.Roles,
            result.SecurityStamp,
            TokenPrincipalFactory.PasswordMethods(result.SecondFactorVerified),
            Now);

        // Grant only scopes that are both requested and explicitly allowed.
        var scopes = PasswordGrant.AuthorizeScopes(request.GetScopes(), AllowedGrantScopes);
        principal.SetScopes(scopes);

        if (await AttachSessionAsync(principal, request) is { } refused)
            return refused;

        ApplyDestinations(principal);
        Audit("token.password", SecurityAuditOutcomes.Success, result.Subject, result.TenantId, request.ClientId);

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<IActionResult> HandleRefreshTokenGrantAsync()
    {
        var info = await HttpContext.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        var current = HttpContext.GetOpenIddictServerRequest();
        var grant = current?.IsAuthorizationCodeGrantType() == true ? "token.authorization-code"
            : current?.IsDeviceCodeGrantType() == true ? "token.device-code"
            : "token.refresh";
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
            // Without a user store nothing can re-check the account (active, lock-out, security stamp), so a refresh
            // token must not renew access: fail closed.
            Audit(grant, SecurityAuditOutcomes.Denied, info.Principal.GetClaim(OpenIddictConstants.Claims.Subject), TenantOf(info.Principal));
            return InvalidRefreshGrant();
        }

        if (await ContinueSessionAsync(principal, grant) is { } sessionRefused)
            return sessionRefused;

        ApplyDestinations(principal);
        Audit(grant, SecurityAuditOutcomes.Success, principal.GetClaim(OpenIddictConstants.Claims.Subject), TenantOf(principal));

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Records the sign-in in the session registry (when it is on) and puts the session id in the tokens as <c>sid</c>. Returns the
    /// refusal to send when the person is at their session limit and the limit refuses new sign-ins.
    /// </summary>
    private async Task<IActionResult?> AttachSessionAsync(ClaimsPrincipal principal, OpenIddictRequest request)
    {
        if (HttpContext.RequestServices.GetService<ISessionRegistry>() is not { } registry
            || !Guid.TryParse(principal.GetClaim(OpenIddictConstants.Claims.Subject), out var userId))
            return null;

        var started = await registry.StartAsync(
            new SessionStart(
                userId, TenantOf(principal), request.ClientId, [.. principal.GetClaims(OpenIddictConstants.Claims.Role)],
                [.. principal.GetClaims(TokenPrincipalFactory.MethodClaim)], HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(), request.GetParameter("device_name")?.ToString()),
            HttpContext.RequestAborted);
        if (started.SessionId is not { } sessionId)
        {
            Audit("token.session-limit", SecurityAuditOutcomes.Denied, userId.ToString(), TenantOf(principal), request.ClientId);
            return ForbidGrant(OpenIddictConstants.Errors.InvalidGrant, started.Refusal ?? "The session limit has been reached.");
        }

        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(TokenPrincipalFactory.SessionClaim, sessionId.ToString()));
        return null;
    }

    /// <summary>
    /// A refresh continues the session its token belongs to (refused when that session ended or timed out); a token without one (an
    /// authorization code, or a refresh token issued before sessions were switched on) starts a session now.
    /// </summary>
    private async Task<IActionResult?> ContinueSessionAsync(ClaimsPrincipal principal, string grant)
    {
        if (HttpContext.RequestServices.GetService<ISessionRegistry>() is not { } registry)
            return null;

        if (Guid.TryParse(principal.GetClaim(TokenPrincipalFactory.SessionClaim), out var sessionId))
        {
            var state = await registry.TouchAsync(
                sessionId, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), HttpContext.RequestAborted);
            if (state == SessionState.Active)
                return null;

            Audit(grant, SecurityAuditOutcomes.Denied, principal.GetClaim(OpenIddictConstants.Claims.Subject), TenantOf(principal));
            return InvalidRefreshGrant();
        }

        return await AttachSessionAsync(principal, HttpContext.GetOpenIddictServerRequest()!);
    }

    /// <summary>
    /// The approval step of the device authorization grant (<c>/connect/verify</c>, RFC 8628). The device showed a user code; a
    /// person who is signed in elsewhere sends it here with their <b>bearer token</b> (not a cookie, so a web page cannot forge
    /// it). <c>GET</c> says which application and scopes the code is for; <c>POST</c> with <c>decision=approve</c> or
    /// <c>deny</c> decides. Approval needs a recent sign-in (<see cref="ModulusIdentityOptions.DeviceApprovalMaxAgeMinutes"/>), and
    /// the device receives the approver's identity and authentication strength but a session of its own.
    /// </summary>
    [HttpGet("~/connect/verify")]
    [HttpPost("~/connect/verify")]
    [IgnoreAntiforgeryToken]
    [Loosened("Device authorization approval: authenticates the approver itself with a bearer token and answers unauthenticated callers 401", Framework = true)]
    [AllowAnonymous]
    public async Task<IActionResult> Verify()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Unable to retrieve OpenIddict request.");
        var approver = await HttpContext.AuthenticateAsync(OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        if (!approver.Succeeded || approver.Principal is null)
            return Unauthorized(new { error = "login_required", error_description = "Send your own access token to approve a device." });

        // The pending authorization: only a valid, unexpired, unused user code authenticates against the server scheme.
        var pending = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (!pending.Succeeded || pending.Principal?.GetClaim(OpenIddictConstants.Claims.Private.DeviceCodeId) is not { Length: > 0 })
        {
            Audit("device.verify", SecurityAuditOutcomes.Denied, approver.Principal.GetClaim(OpenIddictConstants.Claims.Subject), TenantOf(approver.Principal));
            return NotFound(new { error = "invalid_user_code", error_description = "The code is wrong, has expired or was already used." });
        }

        var clientId = request.ClientId ?? pending.Principal.GetPresenters().FirstOrDefault();
        var scopes = PasswordGrant.AuthorizeScopes(pending.Principal.GetScopes(), AllowedGrantScopes);
        if (Microsoft.AspNetCore.Http.HttpMethods.IsGet(Request.Method))
            return Ok(new { client_id = clientId, scopes, user_code = request.UserCode });

        var decision = (Request.HasFormContentType ? Request.Form["decision"].ToString() : Request.Query["decision"].ToString()).Trim().ToLowerInvariant();
        var subject = approver.Principal.GetClaim(OpenIddictConstants.Claims.Subject);
        if (decision == "deny")
        {
            Audit("device.verify", SecurityAuditOutcomes.Denied, subject, TenantOf(approver.Principal), clientId);
            return Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.AccessDenied,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The person refused to authorize the device.",
                }),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (decision != "approve")
            return BadRequest(new { error = "invalid_request", error_description = "Send decision=approve or decision=deny." });

        var maxAge = TimeSpan.FromMinutes(Math.Max(1, HttpContext.RequestServices.GetService<IOptions<ModulusIdentityOptions>>()?.Value.DeviceApprovalMaxAgeMinutes ?? 15));
        var now = Now;
        if (!long.TryParse(approver.Principal.GetClaim(TokenPrincipalFactory.AuthTimeClaim), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var signedInAt)
            || now - DateTimeOffset.FromUnixTimeSeconds(signedInAt) > maxAge)
        {
            return Unauthorized(new { error = "recent_sign_in_required", error_description = $"Sign in again (within {maxAge.TotalMinutes:0} minutes) to approve a device." });
        }

        if (ResolveUserManager() is not { } userManager
            || await TokenPrincipalFactory.RevalidateAsync(userManager, approver.Principal) is not { } principal)
        {
            Audit("device.verify", SecurityAuditOutcomes.Denied, subject, TenantOf(approver.Principal), clientId);
            return Forbid(OpenIddictValidation());
        }

        // The device gets the person's identity and the strength of their sign-in, but never their session.
        if (principal.FindFirst(TokenPrincipalFactory.SessionClaim) is { } sessionClaim)
            ((ClaimsIdentity)principal.Identity!).RemoveClaim(sessionClaim);
        principal.SetScopes(scopes);

        // OpenIddict ties the approval to the pending device authorization through these internal claims.
        foreach (var internalClaim in new[] { OpenIddictConstants.Claims.Private.DeviceCodeId })
        {
            if (pending.Principal.GetClaim(internalClaim) is { Length: > 0 } value)
                principal.SetClaim(internalClaim, value);
        }

        ApplyDestinations(principal);
        Audit("device.verify", SecurityAuditOutcomes.Success, subject, TenantOf(principal), clientId);
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        static string OpenIddictValidation() => OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    }

    /// <summary>The time sign-ins are stamped with and compared against (the host's <see cref="TimeProvider"/>).</summary>
    private DateTimeOffset Now => HttpContext.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;

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
        if (result is null || !await IsInForceAsync(result))
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
    /// A signed token is reported active only while the checks a resource server makes still hold: its stored entry is an
    /// access token that is still valid (not revoked or expired), and its session has not ended.
    /// </summary>
    private async Task<bool> IsInForceAsync(ClaimsIdentity claims)
    {
        // Without a stored entry there is nothing to check revocation against, so the token is not reported active. Id tokens
        // have no entry, and the entry records whether the token is an access token.
        var manager = HttpContext.RequestServices.GetService<IOpenIddictTokenManager>();
        if (manager is null || First(claims.Claims, OpenIddictConstants.Claims.Private.TokenId) is not { Length: > 0 } tokenId)
            return false;
        if (await manager.FindByIdAsync(tokenId) is not { } entry
            || !IsAccessTokenEntryType(await manager.GetTypeAsync(entry))
            || await manager.GetStatusAsync(entry) != OpenIddictConstants.Statuses.Valid)
            return false;

        if (First(claims.Claims, TokenPrincipalFactory.SessionClaim) is { Length: > 0 } sid
            && HttpContext.RequestServices.GetService<ISessionRegistry>() is { } registry)
        {
            // A sid that is not a session id cannot have been issued by this server.
            var state = Guid.TryParse(sid, out var sessionId)
                ? await registry.TouchAsync(sessionId, ct: HttpContext.RequestAborted)
                : SessionState.Unknown;
            if (state != SessionState.Active)
                return false;
        }

        return true;
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

    /// <summary>
    /// OpenIddict stores an access token's type as the RFC 8693 URN (<c>urn:ietf:params:oauth:token-type:access_token</c>), not
    /// the short <c>access_token</c> hint, so both spellings are accepted.
    /// </summary>
    private static bool IsAccessTokenEntryType(string? type)
        => type is OpenIddictConstants.TokenTypeHints.AccessToken or "urn:ietf:params:oauth:token-type:access_token";

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

        // The default issuer matches what OpenIddict stamps on minted tokens: the configured issuer, else the current request's
        // base URL. OpenIddict writes the base URL with a trailing slash ("https://host/"), so the request form ends with one too.
        var issuer = options.Issuer?.AbsoluteUri
            ?? $"{Request.Scheme}://{Request.Host.Value}{(Request.PathBase.Value ?? "").TrimEnd('/')}/";

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
