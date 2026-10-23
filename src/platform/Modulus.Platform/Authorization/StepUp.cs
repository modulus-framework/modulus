namespace Modulus.Authorization;

using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;

/// <summary>
/// Step-up authentication: some permissions (every Critical one, and the ones named here) may be used only by a person who
/// signed in with more than one factor <i>recently</i>. A stolen session or a long-lived refresh token then cannot administer
/// access, approve payments or provision accounts without the second factor.
/// </summary>
public sealed class StepUpOptions
{
    /// <summary>Whether <see cref="PermissionSensitivity.Critical"/> permissions need a fresh multi-factor sign-in. Default <see langword="false"/> (existing tokens carry no authentication claims).</summary>
    public bool RequireForCritical { get; set; }

    /// <summary>More permissions that need it (exact names), Critical or not.</summary>
    public List<string> Permissions { get; set; } = [];

    /// <summary>
    /// Permissions that need a phishing-resistant sign-in: a passkey (WebAuthn), recorded as <c>hwk</c> (device-bound) or <c>swk</c>
    /// (synced). A password with an authenticator app does not qualify. Exact names; empty by default, so nothing changes until an
    /// application names its privileged permissions. Recent sign-in and the machine exemption apply as for <see cref="Permissions"/>.
    /// </summary>
    public List<string> PhishingResistantPermissions { get; set; } = [];

    /// <summary>How old the sign-in may be. Default 15 minutes.</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>The <c>amr</c> value that proves several factors. Default <c>mfa</c>; the token endpoint adds it after a verified second factor or a passkey.</summary>
    public string RequiredMethod { get; set; } = "mfa";

    /// <summary>
    /// Role prefix of machine principals, which have no second factor to give and are exempt (integration clients carry
    /// <c>integration:{clientId}</c>). Their access is limited by grants instead. Default <c>integration:</c>.
    /// </summary>
    public string MachineRolePrefix { get; set; } = "integration:";
}

/// <summary>Decides whether a permission needs step-up and whether a principal satisfies it.</summary>
public sealed class StepUpPolicy(IOptions<ModulusAuthorizationOptions> options, IPermissionRegistry? registry = null, TimeProvider? clock = null)
{
    /// <summary>The authentication method claim.</summary>
    public const string MethodClaim = "amr";

    /// <summary>The time-of-sign-in claim (seconds since the Unix epoch).</summary>
    public const string AuthTimeClaim = "auth_time";

    /// <summary>
    /// Marks a token issued by the client-credentials grant (a machine, not a person). Only the token server issues it, so a
    /// role an administrator or an identity provider assigns cannot stand in for it.
    /// </summary>
    public const string MachineClaim = "modulus:machine";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _gate = new();
    private HashSet<string> _critical = new(StringComparer.OrdinalIgnoreCase);
    private int _seen = -1;

    private StepUpOptions Options => options.Value.StepUp;

    /// <summary>How old a sign-in may be.</summary>
    public TimeSpan MaxAge => Options.MaxAge;

    /// <summary>The <c>amr</c> value a satisfying token carries.</summary>
    public string RequiredMethod => Options.RequiredMethod;

    /// <summary>Whether using <paramref name="permission"/> needs a fresh multi-factor sign-in.</summary>
    public bool Requires(string permission)
    {
        var o = Options;
        if (o.Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase) || RequiresPhishingResistant(permission))
            return true;
        return o.RequireForCritical && CriticalPermissions().Contains(permission);
    }

    /// <summary>Whether using <paramref name="permission"/> needs a fresh passkey sign-in (a phishing-resistant factor).</summary>
    public bool RequiresPhishingResistant(string permission) =>
        Options.PhishingResistantPermissions.Contains(permission, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="principal"/> is exempt (a machine) or signed in recently with the required method.</summary>
    public bool IsSatisfiedBy(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return IsMachine(principal) || IsRecent(principal, method => string.Equals(method, Options.RequiredMethod, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether <paramref name="principal"/> may use <paramref name="permission"/>: a machine, or a recent sign-in that used a passkey
    /// when the permission is phishing-resistant, otherwise the same check as <see cref="IsSatisfiedBy(ClaimsPrincipal)"/>.
    /// </summary>
    public bool IsSatisfiedBy(ClaimsPrincipal principal, string permission)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!RequiresPhishingResistant(permission))
            return IsSatisfiedBy(principal);
        return IsMachine(principal) || IsRecent(principal, method => method is "hwk" or "swk");
    }

    // The exemption needs both the machine marker (token-server issued) and the integration role. A role alone is not enough:
    // a person given an integration:* role must still step up.
    private bool IsMachine(ClaimsPrincipal principal)
    {
        var prefix = Options.MachineRolePrefix;
        return principal.HasClaim(c => c.Type == MachineClaim && string.Equals(c.Value, "true", StringComparison.Ordinal))
            && principal.Claims.Any(c => c.Type is ClaimTypes.Role or "role" && c.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsRecent(ClaimsPrincipal principal, Func<string, bool> methodMatches)
    {
        if (!principal.Claims.Any(c => c.Type == MethodClaim && methodMatches(c.Value)))
            return false;
        if (!long.TryParse(principal.FindFirst(AuthTimeClaim)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return false;

        var age = _clock.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(seconds);
        // A sign-in in the future (clock skew beyond a minute) is not trusted.
        return age >= TimeSpan.FromMinutes(-1) && age <= Options.MaxAge;
    }

    private HashSet<string> CriticalPermissions()
    {
        if (registry is null)
            return _critical;

        var all = registry.GetAll();
        lock (_gate)
        {
            if (_seen != all.Count)
            {
                _critical = new HashSet<string>(
                    all.Where(p => p.Sensitivity == PermissionSensitivity.Critical).Select(p => p.Permission), StringComparer.OrdinalIgnoreCase);
                _seen = all.Count;
            }

            return _critical;
        }
    }
}

/// <summary>
/// Step-up for code that runs through the mediator: reads the caller's principal from the current request. Outside a request
/// (a background job or a message consumer) there is no principal, so a step-up permission is refused: such work must run
/// as a machine (grant it the permission) or be moved behind a request.
/// </summary>
internal sealed class StepUpMediatorGate(StepUpPolicy policy, IHttpContextAccessor accessor) : IStepUpGate
{
    public bool IsSatisfied(string permission)
    {
        if (!policy.Requires(permission))
            return true;
        var principal = accessor.HttpContext?.User;
        return principal?.Identity?.IsAuthenticated == true && policy.IsSatisfiedBy(principal, permission);
    }
}

/// <summary>Marks an authorization failure as "needs a fresh multi-factor sign-in".</summary>
internal sealed class StepUpRequiredReason(IAuthorizationHandler handler)
    : AuthorizationFailureReason(handler, "A recent multi-factor sign-in is required.");

/// <summary>
/// Answers a step-up failure the way RFC 9470 asks: <c>401</c> with <c>WWW-Authenticate: Bearer error="insufficient_user_authentication"</c>
/// and the <c>acr_values</c>/<c>max_age</c> the client should use to re-authenticate, so a client can tell "sign in again with your
/// second factor" from "you are not allowed".
/// </summary>
internal sealed class StepUpResultHandler(StepUpPolicy policy) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy authorizationPolicy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && authorizeResult.AuthorizationFailure?.FailureReasons.Any(r => r is StepUpRequiredReason) == true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate =
                $"Bearer error=\"insufficient_user_authentication\", error_description=\"A recent multi-factor sign-in is required.\", " +
                $"acr_values=\"{policy.RequiredMethod}\", max_age=\"{(int)policy.MaxAge.TotalSeconds}\"";
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://modulus.dev/errors/step-up-required",
                title = "Step-up authentication required",
                status = StatusCodes.Status401Unauthorized,
                code = "STEP_UP_REQUIRED",
                detail = "Sign in again with your second factor and retry.",
            });
            return;
        }

        await _default.HandleAsync(next, context, authorizationPolicy, authorizeResult);
    }
}
