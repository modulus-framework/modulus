namespace Modulus.Identity;

using Modulus.Core.Abstractions;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

/// <summary>
/// Records <c>/connect/revoke</c> (RFC 7009) in the security audit. OpenIddict handles the endpoint on its own (no
/// passthrough controller), so the record comes from two server event handlers: a revoked token is recorded right after
/// OpenIddict's own <c>RevokeToken</c> handler (the token's subject and company are known there), and a refused request
/// is recorded when its error response is applied (the dispatcher stops at a rejection, so the first handler never
/// sees it). Neither records the token itself.
/// </summary>
internal static class RevocationSecurityAudit
{
    public const string Action = "token.revoke";

    public static void Register(OpenIddictServerBuilder options)
    {
        options.AddEventHandler(RevokedHandler.Descriptor);
        options.AddEventHandler(RefusedHandler.Descriptor);
    }

    internal static SecurityAuditEvent Revoked(string? subject, string? tenantClaim, string? tokenType, string? clientId)
        => new()
        {
            Category = SecurityAuditCategories.Identity,
            Action = Action,
            Outcome = SecurityAuditOutcomes.Success,
            Actor = subject,
            TenantId = Guid.TryParse(tenantClaim, out var tenantId) ? tenantId : null,
            Target = tokenType,
            Details = new Dictionary<string, string?> { ["client"] = clientId },
        };

    internal static SecurityAuditEvent Refused(string? error, string? clientId)
        => new()
        {
            Category = SecurityAuditCategories.Identity,
            Action = Action,
            Outcome = SecurityAuditOutcomes.Denied,
            Details = new Dictionary<string, string?> { ["client"] = clientId, ["error"] = error },
        };

    /// <summary>Runs after OpenIddict revoked the token, so only a revocation that happened is recorded.</summary>
    internal sealed class RevokedHandler(ISecurityAuditLog? audit = null) : IOpenIddictServerHandler<HandleRevocationRequestContext>
    {
        public static OpenIddictServerHandlerDescriptor Descriptor { get; }
            = OpenIddictServerHandlerDescriptor.CreateBuilder<HandleRevocationRequestContext>()
                .UseScopedHandler<RevokedHandler>()
                .SetOrder(OpenIddictServerHandlers.Revocation.RevokeToken.Descriptor.Order + 1_000)
                .SetType(OpenIddictServerHandlerType.Custom)
                .Build();

        public ValueTask HandleAsync(HandleRevocationRequestContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            var principal = context.GenericTokenPrincipal;
            audit?.Record(Revoked(
                principal?.GetClaim(OpenIddictConstants.Claims.Subject),
                principal?.GetClaim("tid"),
                principal?.GetTokenType(),
                context.Request.ClientId));
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Records a revocation request OpenIddict refused (invalid client, a token of another client, ...).</summary>
    internal sealed class RefusedHandler(ISecurityAuditLog? audit = null) : IOpenIddictServerHandler<ApplyRevocationResponseContext>
    {
        public static OpenIddictServerHandlerDescriptor Descriptor { get; }
            = OpenIddictServerHandlerDescriptor.CreateBuilder<ApplyRevocationResponseContext>()
                .UseScopedHandler<RefusedHandler>()
                .SetOrder(int.MinValue + 100_000)
                .SetType(OpenIddictServerHandlerType.Custom)
                .Build();

        public ValueTask HandleAsync(ApplyRevocationResponseContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (!string.IsNullOrEmpty(context.Error))
                audit?.Record(Refused(context.Error, context.Request?.ClientId));
            return ValueTask.CompletedTask;
        }
    }
}
