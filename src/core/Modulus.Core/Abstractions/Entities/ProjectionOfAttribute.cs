namespace Modulus.Core.Abstractions.Entities;

/// <summary>
/// Declares that a DTO or other projection shows the fields of <see cref="EntityType"/>. The field-security layer then
/// applies the entity's <see cref="ClassifiedAttribute"/>s to the projection's properties of the same name, and the
/// stricter classification wins. Without it, a classified entity field copied into a DTO that carries no attribute is
/// returned unmasked.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ProjectionOfAttribute(Type entityType) : Attribute
{
    /// <summary>The entity whose classifications the projection inherits.</summary>
    public Type EntityType { get; } = entityType;
}
