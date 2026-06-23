namespace Modulus.Core.Abstractions.Entities;

/// <summary>
/// Implement on any entity to get automatic audit field population
/// in ModuleDbContext.SaveChangesAsync.
/// </summary>
public interface IAuditableEntity
{
    DateTime  CreatedAt  { get; set; }
    string?   CreatedBy  { get; set; }
    DateTime? UpdatedAt  { get; set; }
    string?   UpdatedBy  { get; set; }
}