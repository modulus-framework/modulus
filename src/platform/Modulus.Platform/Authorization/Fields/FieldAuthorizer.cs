namespace Modulus.Authorization.Fields;

using System.Collections.Concurrent;
using System.Reflection;
using Modulus.Authorization.Resources;
using Modulus.Core.Abstractions;

/// <summary>
/// The enforcement point for field-level security. It answers, for the current
/// principal, which fields of a type they may see and set, and applies that decision at
/// the two boundaries the blueprint requires (§5.9): <see cref="Redact{T}"/> masks
/// unreadable fields on a read projection, and <see cref="AuthorizeWriteAsync"/> rejects
/// attempts to set fields the caller may not write. Both directions are always required —
/// a user who cannot see a field must not be able to set it.
/// </summary>
public interface IFieldAuthorizer
{
    /// <summary>
    /// The current principal's resolved read/write access to every field of
    /// <paramref name="type"/>, computed from the type's classifications and profile and
    /// memoised for the request.
    /// </summary>
    FieldMask MaskFor(Type type);

    /// <summary>
    /// Masks every field the current principal may not read on <paramref name="projection"/>
    /// (settable properties are reset to their default; the same instance is returned for
    /// convenience) and returns it. Apply this to the response DTO/projection at the
    /// serialization boundary so masking is uniform across API, report, and export — never
    /// to a tracked entity you intend to persist.
    /// </summary>
    T Redact<T>(T projection);

    /// <summary>
    /// Decides whether the current principal may write every field in
    /// <paramref name="attemptedFields"/> of <paramref name="type"/> — the command/
    /// validation boundary. Fail-closed: unknown field names and fields above the caller's
    /// clearance are refused, and the denial names the offending fields for diagnostics.
    /// Async so a decorator can durably record the decision (<c>AddScopedDecisionAuditing</c>,
    /// blueprint §5.14/§16) — the built-in implementation itself is pure in-memory evaluation.
    /// </summary>
    Task<AccessDecision> AuthorizeWriteAsync(
        Type type, IEnumerable<string> attemptedFields, CancellationToken ct = default);
}

/// <summary>
/// Bridges <see cref="IFieldAuthorizer"/> to the current request: resolves each type's
/// <see cref="FieldMask"/> from the principal's permissions (<see cref="ICurrentUser"/>)
/// against the registered <see cref="FieldSecurityProfile"/>, memoising per type for the
/// request. Scoped. A type with no registered profile still resolves against
/// <see cref="FieldSecurityProfile.Empty"/>, so classification on the model alone keeps
/// sensitive fields fail-closed.
/// </summary>
public sealed class FieldAuthorizer(
    ICurrentUser currentUser,
    IFieldSecurityRegistry registry) : IFieldAuthorizer
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> SettableProperties = new();

    private readonly Dictionary<Type, FieldMask> _masks = [];

    public FieldMask MaskFor(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (_masks.TryGetValue(type, out var cached))
            return cached;

        var profile = registry.Find(type) ?? FieldSecurityProfile.Empty;
        var classifications = FieldClassificationMap.For(type);

        var fields = new Dictionary<string, FieldAccess>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, classification) in classifications)
        {
            var canRead = profile.ReadRequirement(name, classification).IsSatisfiedBy(currentUser.HasPermission);
            var canWrite = profile.WriteRequirement(name, classification).IsSatisfiedBy(currentUser.HasPermission);
            fields[name] = new FieldAccess(name, classification, canRead, canWrite);
        }

        var mask = new FieldMask(fields);
        _masks[type] = mask;
        return mask;
    }

    public T Redact<T>(T projection)
    {
        if (projection is null)
            return projection;

        var type = projection.GetType();
        var mask = MaskFor(type);
        foreach (var property in SettablePropertiesOf(type))
        {
            if (!mask.CanRead(property.Name))
                property.SetValue(projection, DefaultValue(property.PropertyType));
        }

        return projection;
    }

    public Task<AccessDecision> AuthorizeWriteAsync(
        Type type, IEnumerable<string> attemptedFields, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(attemptedFields);

        var mask = MaskFor(type);
        var denied = attemptedFields.Where(field => !mask.CanWrite(field)).ToList();
        return Task.FromResult(denied.Count == 0
            ? AccessDecision.Allow()
            : AccessDecision.Deny(
                $"write to protected field(s) {string.Join(", ", denied)} on '{type.Name}' is not permitted"));
    }

    private static PropertyInfo[] SettablePropertiesOf(Type type)
        => SettableProperties.GetOrAdd(type, static t =>
            [.. t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite && p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0)]);

    private static object? DefaultValue(Type type)
        => type.IsValueType && Nullable.GetUnderlyingType(type) is null
            ? Activator.CreateInstance(type)
            : null;
}
