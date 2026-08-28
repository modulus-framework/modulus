namespace Modulus.EntityFrameworkCore.ChangeHistory;

/// <summary>
/// Marks a property or class for change tracking. When applied to a class,
/// all properties of that class are tracked; when applied to individual
/// properties, only those properties are tracked.
///
/// Change history is captured automatically in EntityChange
/// whenever auditable entity rows are created or modified.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public sealed class AuditedAttribute : Attribute
{
}
