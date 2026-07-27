namespace Modulus.Authorization.Audit;

using Modulus.Authorization.Resources;
using Modulus.Core.Abstractions;

/// <summary>
/// Decorates an <see cref="IResourceAuthorizer"/> with scoped decision auditing
/// (blueprint §5.14/§16): after the inner authorizer decides, emits an
/// <see cref="AccessDecisionAuditEvent"/> only when <paramref name="registry"/>
/// marks the resource type + action as audit-worthy — installed by
/// <c>AddScopedDecisionAuditing</c>, mirroring how <c>DelegationAwarePermissionResolver</c>
/// decorates <c>IPermissionResolver</c> (same "wrap the concrete instance,
/// <c>services.Replace</c> the interface" registration pattern).
/// </summary>
public sealed class AuditingResourceAuthorizer(
    IResourceAuthorizer inner,
    IAuditableActionRegistry registry,
    IAuthorizationAuditWriter auditWriter,
    ICurrentUser currentUser)
    : IResourceAuthorizer
{
    public async Task<AccessDecision> AuthorizeAsync(
        object resource, string action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var decision = await inner.AuthorizeAsync(resource, action, ct);

        if (registry.IsAuditWorthy(resource.GetType(), action))
        {
            await auditWriter.WriteAsync(
                new AccessDecisionAuditEvent(
                    resource.GetType().Name,
                    action,
                    decision.IsAllowed,
                    decision.Reason,
                    currentUser.UserId?.ToString()),
                ct);
        }

        return decision;
    }
}
