namespace Modulus.Core.Abstractions.Entities;

/// <summary>
/// Implement on any entity to get automatic tenant isolation.
/// EF Core applies a global query filter: WHERE tenant_id = @currentTenantId.
/// </summary>
public interface IHasTenantId
{
    Guid TenantId { get; set; }
}
