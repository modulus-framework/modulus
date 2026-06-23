namespace Modulus.Core.Abstractions.Entities;

/// <summary>
/// Implement on any entity to get a global EF Core soft-delete filter.
/// Deleted records are invisible to all queries unless explicitly bypassed.
/// </summary>
public interface ISoftDelete
{
    bool      IsDeleted  { get; set; }
    DateTime? DeletedAt  { get; set; }
    string?   DeletedBy  { get; set; }
}