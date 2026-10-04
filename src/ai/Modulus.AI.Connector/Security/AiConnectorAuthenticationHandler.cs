namespace Modulus.AI.Connector;

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Modulus.AI.Connector.Contract;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;

/// <summary>The connector's authentication scheme and the claims it puts on the principal.</summary>
public static class AiConnectorDefaults
{
    /// <summary>The scheme name.</summary>
    public const string AuthenticationScheme = "ModulusAiConnector";

    /// <summary>
    /// The scheme of calls the platform makes as itself, with no user (manifest, health): the API key only.
    /// </summary>
    public const string ServiceAuthenticationScheme = "ModulusAiConnector.Service";

    /// <summary>The authorization policy of the user calls (no <c>:</c>, so it is not a permission policy).</summary>
    public const string Policy = "ModulusAiConnector";

    /// <summary>The authorization policy of the platform's own calls.</summary>
    public const string ServicePolicy = "ModulusAiConnector.Service";

    /// <summary>Claim on the platform's own calls (no user).</summary>
    public const string ServiceClaim = "ai_service";

    /// <summary>Claim: the platform app instance the call is for.</summary>
    public const string AppInstanceClaim = "ai_app_instance";

    /// <summary>Claim: the platform tenant.</summary>
    public const string PlatformTenantClaim = "ai_platform_tenant";

    /// <summary>Claim: the envelope id (for the audit trail).</summary>
    public const string EnvelopeClaim = "ai_envelope";

    /// <summary>The company claim, the same one tokens carry.</summary>
    public const string TenantClaim = "tid";
}

/// <summary>Options of one connector scheme.</summary>
internal sealed class AiConnectorAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>Whether a user envelope is required (user calls) or not (the platform's own calls).</summary>
    public bool RequireEnvelope { get; set; } = true;
}

/// <summary>The verified facts of one connector call, kept on <see cref="HttpContext.Items"/>.</summary>
internal sealed record AiConnectorCall(
    AiAppInstanceOptions Instance,
    AiConnectorUser User,
    AiEnvelope Envelope)
{
    private static readonly object Key = new();

    public static AiConnectorCall? Of(HttpContext context) => context.Items.TryGetValue(Key, out var value) ? value as AiConnectorCall : null;

    public void Attach(HttpContext context) => context.Items[Key] = this;
}

/// <summary>
/// Authenticates the AI platform and the user it acts for (AD-25): no browser <c>Origin</c>; an API key whose hash is
/// configured; a valid, unreplayed envelope for a configured app instance of the right platform tenant; an active
/// account for the envelope's user; and, when the instance maps to a company, an active company the user is a member
/// of. The principal is that account (id, roles, company), so the app's permission checks run unchanged. Any failure
/// is a <c>401</c> <c>DENIED</c>, and its reason goes to the security audit trail only. The service scheme stops after
/// the API key: it authenticates the platform itself, for calls that name no user.
/// </summary>
internal sealed class AiConnectorAuthenticationHandler(
    IOptionsMonitor<AiConnectorAuthenticationOptions> schemeOptions,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptions<ModulusAiConnectorOptions> connectorOptions,
    AiEnvelopeValidator envelopes,
    ISecurityAuditLog audit)
    : AuthenticationHandler<AiConnectorAuthenticationOptions>(schemeOptions, loggerFactory, encoder)
{
    private const string ApiKeyPrefix = "ApiKey ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var options = connectorOptions.Value;
        var request = Context.Request;

        if (request.Headers.Origin.Count > 0)
            return Deny("browser origin", null);

        var authorization = request.Headers.Authorization.ToString();
        if (!authorization.StartsWith(ApiKeyPrefix, StringComparison.OrdinalIgnoreCase)
            || !AiApiKeys.Matches(authorization[ApiKeyPrefix.Length..].Trim(), options.ApiKeyHashes))
            return Deny("missing or unknown api key", null);

        if (!Options.RequireEnvelope)
        {
            var service = new ClaimsIdentity(Scheme.Name);
            service.AddClaim(new Claim(AiConnectorDefaults.ServiceClaim, "true"));
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(service), Scheme.Name));
        }

        var token = request.Headers[options.Platform.EnvelopeHeader].ToString();
        if (string.IsNullOrWhiteSpace(token))
            return Deny("missing envelope", null);

        var verified = await envelopes.ValidateAsync(token, Context.RequestAborted);
        if (verified.Envelope is not { } envelope)
            return Deny(verified.Failure ?? "invalid envelope", null);

        var instance = options.Instances.FirstOrDefault(i =>
            string.Equals(i.AppInstanceId, envelope.AppInstanceId, StringComparison.Ordinal)
            && string.Equals(i.PlatformTenantId, envelope.PlatformTenantId, StringComparison.Ordinal));
        if (instance is null)
            return Deny("app instance not mapped to this platform tenant", envelope);

        if (!envelope.Claims.TryGetValue(options.Users.Claim, out var userValue) || string.IsNullOrWhiteSpace(userValue))
            return Deny("envelope names no user", envelope);

        var services = Context.RequestServices;
        var user = await services.GetRequiredService<IAiConnectorUserResolver>()
            .ResolveAsync(new AiUserLookup(userValue, options.Users.MatchBy, envelope.Claims), Context.RequestAborted);
        if (user is null)
            return Deny("unknown or inactive user", envelope);

        if (instance.TenantId is { } tenantId)
        {
            try
            {
                await services.VerifyTenantAsync(tenantId, Context.RequestAborted);
            }
            catch (TenantContextRejectedException)
            {
                return Deny("company unknown or inactive", envelope, tenantId);
            }

            if (options.RequireMembership
                && (services.GetService<ITenantMembershipStore>() is not { } memberships
                    || !await memberships.IsMemberAsync(user.UserId, tenantId, Context.RequestAborted)))
                return Deny("not a member of the company", envelope, tenantId);
        }

        var identity = new ClaimsIdentity(AiConnectorDefaults.AuthenticationScheme, "name", "role");
        var userId = user.UserId.ToString("D");
        identity.AddClaim(new Claim("sub", userId));
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
        if (user.UserName is not null)
            identity.AddClaim(new Claim("name", user.UserName));
        if (user.Email is not null)
            identity.AddClaim(new Claim("email", user.Email));
        foreach (var role in user.Roles)
            identity.AddClaim(new Claim("role", role));
        if (instance.TenantId is { } tid)
            identity.AddClaim(new Claim(AiConnectorDefaults.TenantClaim, tid.ToString("D")));
        identity.AddClaim(new Claim(AiConnectorDefaults.AppInstanceClaim, instance.AppInstanceId));
        identity.AddClaim(new Claim(AiConnectorDefaults.PlatformTenantClaim, instance.PlatformTenantId));
        identity.AddClaim(new Claim(AiConnectorDefaults.EnvelopeClaim, envelope.Id));

        new AiConnectorCall(instance, user, envelope).Attach(Context);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        => WriteErrorAsync(StatusCodes.Status401Unauthorized, "The request could not be authenticated.");

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        => WriteErrorAsync(StatusCodes.Status403Forbidden, "The request is not allowed.");

    private Task WriteErrorAsync(int status, string message)
    {
        Response.StatusCode = status;
        return Response.WriteAsJsonAsync(new ConnectorError(ConnectorErrorCodes.Denied, message), ConnectorJson.Options);
    }

    private AuthenticateResult Deny(string reason, AiEnvelope? envelope, Guid? tenantId = null)
    {
        audit.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Ai,
            Action = "connector.authenticate",
            Outcome = SecurityAuditOutcomes.Denied,
            TenantId = tenantId,
            CorrelationId = envelope?.CorrelationId,
            Details = new Dictionary<string, string?>
            {
                ["reason"] = reason,
                ["appInstanceId"] = envelope?.AppInstanceId,
                ["envelopeId"] = envelope?.Id,
            },
        });
        return AuthenticateResult.Fail(reason);
    }
}
