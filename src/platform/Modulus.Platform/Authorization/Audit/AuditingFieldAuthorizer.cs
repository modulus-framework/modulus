namespace Modulus.Authorization.Audit;

using Modulus.Authorization.Fields;
using Modulus.Authorization.Resources;
using Modulus.Core.Abstractions;

/// <summary>
/// Decorates an <see cref="IFieldAuthorizer"/> with scoped decision auditing on
/// its write boundary (blueprint §5.14/§16) — see <see cref="AuditingResourceAuthorizer"/>
/// remarks for the installation pattern. <see cref="Redact{T}"/>/<see cref="MaskFor"/>
/// (the read boundary) are passed through unaudited: read-path field-level
/// auditing is a separate, further-scoped increment (every read touching a
/// classified field would be far higher volume than write decisions).
/// </summary>
public sealed class AuditingFieldAuthorizer(
    IFieldAuthorizer inner,
    IAuditableActionRegistry registry,
    IAuthorizationAuditWriter auditWriter,
    ICurrentUser currentUser)
    : IFieldAuthorizer
{
    public FieldMask MaskFor(Type type) => inner.MaskFor(type);

    public T Redact<T>(T projection) => inner.Redact(projection);

    public async Task<AccessDecision> AuthorizeWriteAsync(
        Type type, IEnumerable<string> attemptedFields, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(type);

        var fields = attemptedFields as ICollection<string> ?? attemptedFields.ToList();
        var decision = await inner.AuthorizeWriteAsync(type, fields, ct);

        if (registry.IsAuditWorthy(type, AuditableActions.FieldWrite))
        {
            await auditWriter.WriteAsync(
                new AccessDecisionAuditEvent(
                    type.Name,
                    $"{AuditableActions.FieldWrite}:{string.Join(",", fields)}",
                    decision.IsAllowed,
                    decision.Reason,
                    currentUser.UserId?.ToString()),
                ct);
        }

        return decision;
    }
}
