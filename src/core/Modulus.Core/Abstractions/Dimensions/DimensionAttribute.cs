namespace Modulus.Core.Abstractions.Dimensions;

/// <summary>
/// Declares that a <see cref="Guid"/> (or <see cref="Nullable{T}"/> of <see cref="Guid"/>) property holds the id of a value of the named
/// scope dimension, such as <c>warehouse</c>. A create or update of the entity is refused unless that id is registered for the caller's
/// company, so a record cannot carry a warehouse or region that scope assignments and access keys do not know. A null value is not checked.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class DimensionAttribute(string dimension) : Attribute
{
    /// <summary>The dimension the property's value belongs to, such as <c>warehouse</c>.</summary>
    public string Dimension { get; } = dimension;
}
