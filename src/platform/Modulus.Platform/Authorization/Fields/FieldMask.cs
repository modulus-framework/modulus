namespace Modulus.Authorization.Fields;

using Modulus.Core.Abstractions.Entities;

/// <summary>
/// The resolved read/write access a specific principal has to each field of a type,
/// computed once per request from the type's classification map and its field security
/// profile (blueprint §5.9, §11). It is the cached artefact enforcement reads: the read
/// projection masks fields where <see cref="CanRead"/> is false; the write boundary
/// rejects fields where <see cref="CanWrite"/> is false. Unknown field names are
/// fail-closed (neither readable nor writable).
/// </summary>
public sealed class FieldMask
{
    private readonly IReadOnlyDictionary<string, FieldAccess> _fields;

    internal FieldMask(IReadOnlyDictionary<string, FieldAccess> fields) => _fields = fields;

    /// <summary>Per-field resolved access, for masking, write checks, and audit/matrix review.</summary>
    public IReadOnlyCollection<FieldAccess> Fields => (IReadOnlyCollection<FieldAccess>)_fields.Values;

    /// <summary>True when the principal may read <paramref name="field"/>. Unknown fields fail closed.</summary>
    public bool CanRead(string field)
        => _fields.TryGetValue(field, out var access) && access.CanRead;

    /// <summary>True when the principal may write <paramref name="field"/>. Unknown fields fail closed.</summary>
    public bool CanWrite(string field)
        => _fields.TryGetValue(field, out var access) && access.CanWrite;
}

/// <summary>The resolved access to one field for one principal.</summary>
/// <param name="Field">The property name.</param>
/// <param name="Classification">The field's declared sensitivity.</param>
/// <param name="CanRead">Whether the principal may read (see) the field.</param>
/// <param name="CanWrite">Whether the principal may write (set) the field.</param>
public sealed record FieldAccess(
    string Field, FieldClassification Classification, bool CanRead, bool CanWrite);
