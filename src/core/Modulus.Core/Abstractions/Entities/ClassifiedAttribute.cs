namespace Modulus.Core.Abstractions.Entities;

/// <summary>
/// Classifies a property's sensitivity for field-level security (blueprint §5.9, §11).
/// Placed on a model or DTO property, it declares the field's
/// <see cref="FieldClassification"/> so the field-security layer can mask it on read and
/// reject it on write for principals without the required clearance. Unclassified
/// properties are treated as <see cref="FieldClassification.Public"/>. Classification
/// lives on the model — declarative and uniform across every projection — never in
/// per-endpoint code.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ClassifiedAttribute(FieldClassification classification) : Attribute
{
    /// <summary>The sensitivity classification of the annotated field.</summary>
    public FieldClassification Classification { get; } = classification;
}
