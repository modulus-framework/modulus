namespace Modulus.Bff.Authentication;

using Microsoft.AspNetCore.Authorization;

/// <summary>
/// The per-client policy requirement: the caller authenticated through this client's own scheme
/// and, for bearer clients, the token was issued to an allowed client id and carries the required
/// scopes. A token or cookie of one client therefore never passes another client's policy, even
/// when they share a gateway host.
/// </summary>
public sealed class BffClientRequirement(string client) : IAuthorizationRequirement
{
    public string Client { get; } = client;
}

internal sealed class BffClientRequirementHandler(IOptionsMonitor<BffClientOptions> clients, ILogger<BffClientRequirementHandler> logger)
    : AuthorizationHandler<BffClientRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, BffClientRequirement requirement)
    {
        var scheme = BffDefaults.Scheme(requirement.Client);
        var identity = context.User.Identities.FirstOrDefault(i => i.IsAuthenticated && i.AuthenticationType == scheme);
        if (identity is null)
            return Task.CompletedTask;

        var options = clients.Get(requirement.Client);
        if (options.Kind != BffClientKind.Web)
        {
            var principal = new System.Security.Claims.ClaimsPrincipal(identity);
            var allowed = options.AllowedClientIds.Count > 0
                ? options.AllowedClientIds
                : options.ClientId is { Length: > 0 } id ? [id] : [];
            if (allowed.Count > 0 && (BffClaims.GetClientId(principal) is not { } clientId || !allowed.Contains(clientId, StringComparer.Ordinal)))
            {
                logger.LogInformation("BFF client {Client} rejected a token issued to another client", requirement.Client);
                return Task.CompletedTask;
            }

            var scopes = BffClaims.GetScopes(principal);
            if (options.RequiredScopes.Exists(s => !scopes.Contains(s)))
            {
                logger.LogInformation("BFF client {Client} rejected a token missing a required scope", requirement.Client);
                return Task.CompletedTask;
            }
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
