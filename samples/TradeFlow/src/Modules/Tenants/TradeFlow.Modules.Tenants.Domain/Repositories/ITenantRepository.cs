using TradeFlow.Modules.Tenants.Domain.Constants;
using TradeFlow.Modules.Tenants.Domain.Entities;
using TradeFlow.Modules.Tenants.Domain.ValueObjects;

namespace TradeFlow.Modules.Tenants.Domain.Repositories;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(TenantId id, CancellationToken cancellationToken = default);
    Task<Tenant?> GetBySubdomainAsync(Subdomain subdomain, CancellationToken cancellationToken = default);
    Task<Tenant?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<bool> ExistsBySubdomainAsync(Subdomain subdomain, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Tenant>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Tenant>> GetActiveTenantsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Tenant>> GetInactiveTenantsAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default);
    Task UpdateAsync(Tenant tenant, CancellationToken cancellationToken = default);
    Task DeleteAsync(Tenant tenant, CancellationToken cancellationToken = default);
}
