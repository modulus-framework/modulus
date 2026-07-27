namespace Modulus.Authorization.Audit;

/// <summary>
/// Declares which resource-type/action pairs are "audit-worthy" — the
/// declarative scoping mechanism the blueprint requires for decision auditing
/// (§5.14/§16: auditing every decision is "prohibitively voluminous", so it
/// must be possible to scope auditing to sensitive resources/operations).
/// Consulted by the decorators <c>AddScopedDecisionAuditing</c> installs over
/// <see cref="Modulus.Authorization.Resources.IResourceAuthorizer"/> and
/// <see cref="Modulus.Authorization.Fields.IFieldAuthorizer"/>.
/// </summary>
public interface IAuditableActionRegistry
{
    bool IsAuditWorthy(Type resourceType, string action);
}

/// <summary>
/// Fixed sentinel action recorded when a field-write decision on a marked type
/// is denied or allowed — field writes don't have a caller-supplied action
/// name the way resource actions do (<c>"approve"</c>, <c>"edit"</c>, …).
/// </summary>
public static class AuditableActions
{
    public const string FieldWrite = "FieldWrite";
}

/// <summary>Mutable, accumulating registry — register entries via <see cref="Mark"/>.</summary>
public sealed class AuditableActionRegistry : IAuditableActionRegistry
{
    private readonly HashSet<(Type ResourceType, string Action)> _entries = [];

    /// <summary>Marks <paramref name="resourceType"/> + <paramref name="action"/> as audit-worthy.</summary>
    public AuditableActionRegistry Mark(Type resourceType, string action)
    {
        _entries.Add((resourceType, action));
        return this;
    }

    /// <summary>Marks every field-write decision on <typeparamref name="T"/> as audit-worthy.</summary>
    public AuditableActionRegistry MarkFieldWrites<T>() => Mark(typeof(T), AuditableActions.FieldWrite);

    public bool IsAuditWorthy(Type resourceType, string action) => _entries.Contains((resourceType, action));
}

/// <summary>No-op default — nothing is audit-worthy until <c>AddScopedDecisionAuditing</c> marks entries.</summary>
public sealed class NullAuditableActionRegistry : IAuditableActionRegistry
{
    public static readonly NullAuditableActionRegistry Instance = new();

    public bool IsAuditWorthy(Type resourceType, string action) => false;
}
