namespace Modulus.Authorization.Fields;

using Modulus.Core.Abstractions.Entities;

/// <summary>
/// Fluent builder for a <see cref="FieldSecurityProfile"/>. Clearances read as the
/// disclosure policy they encode, for example:
/// <code>
/// FieldSecurityProfile.Define(p => p
///     .Classification(FieldClassification.Confidential, read: "candidate:confidential:read")
///     .Classification(FieldClassification.Restricted,
///                     read: "candidate:comp:read", write: "candidate:comp:write")
///     .Field("Ssn", read: "candidate:ssn:read", write: "candidate:ssn:write"));
/// </code>
/// A classification or field left unconfigured stays at the built-in default — Public
/// open, everything more sensitive closed — so the profile only ever <i>opens</i> access.
/// </summary>
public sealed class FieldSecurityProfileBuilder
{
    private readonly Dictionary<FieldClassification, FieldClearance> _byClassification = [];
    private readonly Dictionary<string, FieldClearance> _byField =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Grants read and/or write of every field carrying
    /// <paramref name="classification"/> to holders of the given permission(s). Pass only
    /// the direction(s) you intend to open; an omitted direction stays closed (for
    /// sensitive classifications) so opening read never implicitly opens write.
    /// </summary>
    public FieldSecurityProfileBuilder Classification(
        FieldClassification classification, string? read = null, string? write = null)
    {
        _byClassification[classification] = Merge(
            _byClassification.GetValueOrDefault(classification), read, write);
        return this;
    }

    /// <summary>
    /// Overrides the clearance for one specific field by name, taking precedence over its
    /// classification rule — for the field that needs a narrower or broader permission
    /// than its class (e.g. a single restricted column with its own permission).
    /// </summary>
    public FieldSecurityProfileBuilder Field(string field, string? read = null, string? write = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        _byField[field] = Merge(_byField.GetValueOrDefault(field), read, write);
        return this;
    }

    internal FieldSecurityProfile Build() => new(_byClassification, _byField);

    private static FieldClearance Merge(FieldClearance? existing, string? read, string? write)
    {
        Validate(read);
        Validate(write);
        return new FieldClearance(read ?? existing?.Read, write ?? existing?.Write);
    }

    private static void Validate(string? permission)
    {
        if (permission is not null && string.IsNullOrWhiteSpace(permission))
            throw new ArgumentException("Permission must be non-empty when provided.", nameof(permission));
    }
}
