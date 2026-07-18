namespace Modulus.Authorization.Fields;

using Modulus.Core.Abstractions.Entities;

/// <summary>
/// A declarative field security profile (Dynamics's term) for one resource type — the
/// field-level layer of the pipeline (blueprint §5.9, §11). It maps a field's
/// <see cref="FieldClassification"/>, and any per-field override, to the read/write
/// <see cref="FieldRequirement"/> a principal must clear. Resolution is
/// <b>deny-by-default</b>: a sensitive classification with no configured clearance is
/// <see cref="FieldRequirement.Closed"/> until the profile opens it, so merely
/// classifying a field on the model protects it even before a profile exists. Public
/// fields are open. A profile is pure and reusable; register one per type with
/// <c>AddFieldSecurity&lt;T&gt;</c>.
/// </summary>
public sealed class FieldSecurityProfile
{
    private readonly IReadOnlyDictionary<FieldClassification, FieldClearance> _byClassification;
    private readonly IReadOnlyDictionary<string, FieldClearance> _byField;

    internal FieldSecurityProfile(
        IReadOnlyDictionary<FieldClassification, FieldClearance> byClassification,
        IReadOnlyDictionary<string, FieldClearance> byField)
    {
        _byClassification = byClassification;
        _byField = byField;
    }

    /// <summary>
    /// The empty profile: no clearances configured, so it applies the built-in
    /// deny-by-default rules alone (Public open, everything more sensitive closed). Used
    /// when a type has no registered profile — classification on the model still
    /// protects fields.
    /// </summary>
    public static readonly FieldSecurityProfile Empty = new(
        new Dictionary<FieldClassification, FieldClearance>(),
        new Dictionary<string, FieldClearance>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Builds a profile from a fluent clearance declaration.</summary>
    public static FieldSecurityProfile Define(Action<FieldSecurityProfileBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new FieldSecurityProfileBuilder();
        configure(builder);
        return builder.Build();
    }

    /// <summary>
    /// The clearance a principal must hold to <b>read</b> the field named
    /// <paramref name="field"/> carrying classification <paramref name="classification"/>.
    /// Per-field override wins over the classification rule, which wins over the built-in
    /// default (Public open, otherwise closed).
    /// </summary>
    public FieldRequirement ReadRequirement(string field, FieldClassification classification)
        => Resolve(field, classification, write: false);

    /// <summary>
    /// The clearance a principal must hold to <b>write</b> the field named
    /// <paramref name="field"/> carrying classification <paramref name="classification"/>.
    /// Same precedence as <see cref="ReadRequirement"/>.
    /// </summary>
    public FieldRequirement WriteRequirement(string field, FieldClassification classification)
        => Resolve(field, classification, write: true);

    private FieldRequirement Resolve(string field, FieldClassification classification, bool write)
    {
        if (_byField.TryGetValue(field, out var fieldClearance)
            && Pick(fieldClearance, write) is { } fieldPermission)
            return FieldRequirement.Require(fieldPermission);

        if (_byClassification.TryGetValue(classification, out var classClearance)
            && Pick(classClearance, write) is { } classPermission)
            return FieldRequirement.Require(classPermission);

        return classification == FieldClassification.Public
            ? FieldRequirement.Open
            : FieldRequirement.Closed;
    }

    private static string? Pick(FieldClearance clearance, bool write)
        => write ? clearance.Write : clearance.Read;
}

/// <summary>
/// A configured read/write permission pair for a classification or a specific field. A
/// <see langword="null"/> direction means "not configured here" — resolution falls
/// through to the next-lower precedence level rather than being treated as open.
/// </summary>
/// <param name="Read">Permission required to read, or <see langword="null"/> if unset at this level.</param>
/// <param name="Write">Permission required to write, or <see langword="null"/> if unset at this level.</param>
internal sealed record FieldClearance(string? Read, string? Write);
