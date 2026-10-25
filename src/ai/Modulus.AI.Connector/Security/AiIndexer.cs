namespace Modulus.AI.Connector;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Modulus.AI.Connector.Contract;
using Modulus.AI.Connector.Execution;
using Modulus.Core.Abstractions;

/// <summary>
/// The service identity the platform's ingestion reads as (<c>/extract</c>, <c>/changes</c>): no user, the configured
/// <see cref="AiIndexingOptions.Roles"/> in the instance's company, so the app's grant store decides what the index may
/// hold. The platform authenticates with its API key only; the app instance is named by the <c>appInstanceId</c> query
/// parameter and must be configured.
/// </summary>
internal static class AiIndexer
{
    /// <summary>The actor recorded in the audit trail.</summary>
    public const string Actor = "ai-indexer";

    /// <summary>The claim marking the indexing identity.</summary>
    public const string Claim = "ai_indexer";

    public static async Task<IResult> RunAsync(
        HttpContext http,
        string? appInstanceId,
        Func<AiConnectorService, AiAppInstanceOptions, Task<IResult>> work)
    {
        var services = http.RequestServices;
        var options = services.GetRequiredService<IOptions<ModulusAiConnectorOptions>>().Value;
        var instance = options.Instances.FirstOrDefault(i => string.Equals(i.AppInstanceId, appInstanceId, StringComparison.Ordinal));
        if (instance is null)
        {
            services.GetRequiredService<ISecurityAuditLog>().Record(new SecurityAuditEvent
            {
                Category = SecurityAuditCategories.Ai,
                Action = "connector.indexer",
                Outcome = SecurityAuditOutcomes.Denied,
                Actor = Actor,
                Details = new Dictionary<string, string?> { ["reason"] = "unknown app instance", ["appInstanceId"] = appInstanceId },
            });
            return Results.Json(
                new ConnectorError(ConnectorErrorCodes.Denied, "Unknown app instance."),
                ConnectorJson.Options,
                statusCode: StatusCodes.Status403Forbidden);
        }

        // The key must be the one bound to this instance: with several instances, one shared key would read every company's index.
        if (!KeyBelongsTo(http.Request.Headers.Authorization.ToString(), instance, options.Instances.Count))
        {
            services.GetRequiredService<ISecurityAuditLog>().Record(new SecurityAuditEvent
            {
                Category = SecurityAuditCategories.Ai,
                Action = "connector.indexer",
                Outcome = SecurityAuditOutcomes.Denied,
                Actor = Actor,
                Details = new Dictionary<string, string?> { ["reason"] = "api key not bound to app instance", ["appInstanceId"] = appInstanceId },
            });
            return Results.Json(
                new ConnectorError(ConnectorErrorCodes.Denied, "This API key is not bound to the app instance."),
                ConnectorJson.Options,
                statusCode: StatusCodes.Status403Forbidden);
        }

        http.User = Principal(instance, options.Indexing);

        // Resolved only now, so every scoped service that reads the user (permission checks, field masks) sees the
        // indexing identity rather than the bare API-key principal.
        return await work(services.GetRequiredService<AiConnectorService>(), instance);
    }

    /// <summary>
    /// True when the <c>Authorization</c> header carries the API key bound to <paramref name="instance"/>. An instance with no binding
    /// is served by the connector's shared keys only when it is the single instance (the single-company case).
    /// </summary>
    internal static bool KeyBelongsTo(string? authorization, AiAppInstanceOptions instance, int instanceCount)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (string.IsNullOrEmpty(instance.ApiKeyHash))
            return instanceCount == 1;
        const string prefix = "ApiKey ";
        if (authorization is null || !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var presented = AiApiKeys.Hash(authorization[prefix.Length..].Trim());
        return string.Equals(presented, instance.ApiKeyHash, StringComparison.OrdinalIgnoreCase);
    }

    private static ClaimsPrincipal Principal(AiAppInstanceOptions instance, AiIndexingOptions indexing)
    {
        var identity = new ClaimsIdentity(AiConnectorDefaults.ServiceAuthenticationScheme, "name", "role");
        identity.AddClaim(new Claim("name", Actor));
        if (indexing.ServiceUserId is { } userId)
        {
            identity.AddClaim(new Claim("sub", userId.ToString("D")));
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")));
        }

        foreach (var role in indexing.Roles.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.Ordinal))
            identity.AddClaim(new Claim("role", role));
        if (instance.TenantId is { } tenantId)
            identity.AddClaim(new Claim(AiConnectorDefaults.TenantClaim, tenantId.ToString("D")));
        identity.AddClaim(new Claim(AiConnectorDefaults.AppInstanceClaim, instance.AppInstanceId));
        identity.AddClaim(new Claim(AiConnectorDefaults.ServiceClaim, "true"));
        identity.AddClaim(new Claim(Claim, "true"));
        return new ClaimsPrincipal(identity);
    }
}
