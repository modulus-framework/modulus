using ModulusSample.Modules.Tenants.Domain.Entities;
using ModulusSample.Modules.Tenants.Domain.Repositories;
using ModulusSample.Modules.Tenants.Domain.ValueObjects;
using ModulusSample.Modules.Tenants.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace ModulusSample.Modules.Tenants.Infrastructure.Repositories;

public sealed class EfTenantRepository(TenantsDbContext context) : ITenantRepository
{
    public async Task<Tenant?> GetByIdAsync(TenantId id, CancellationToken cancellationToken = default)
    {
        return await context.Tenants
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<Tenant?> GetBySubdomainAsync(Subdomain subdomain, CancellationToken cancellationToken = default)
    {
        return await context.Tenants
            .FirstOrDefaultAsync(t => t.Subdomain == subdomain && !t.IsDeleted, cancellationToken);
    }

    public async Task<Tenant?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await context.Tenants
            .FirstOrDefaultAsync(t => t.Name == name && !t.IsDeleted, cancellationToken);
    }

    public async Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await context.Tenants
            .AnyAsync(t => t.Name == name && !t.IsDeleted, cancellationToken);
    }

    public async Task<bool> ExistsBySubdomainAsync(Subdomain subdomain, CancellationToken cancellationToken = default)
    {
        return await context.Tenants
            .AnyAsync(t => t.Subdomain == subdomain && !t.IsDeleted, cancellationToken);
    }

    public async Task<IReadOnlyList<Tenant>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await context.Tenants
            .Where(t => !t.IsDeleted)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Tenant>> GetActiveTenantsAsync(CancellationToken cancellationToken = default)
    {
        return await context.Tenants
            .Where(t => t.IsActive && !t.IsDeleted)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Tenant>> GetInactiveTenantsAsync(CancellationToken cancellationToken = default)
    {
        return await context.Tenants
            .Where(t => !t.IsActive && !t.IsDeleted)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        await context.Tenants.AddAsync(tenant, cancellationToken);
    }

    public async Task UpdateAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        context.Tenants.Update(tenant);
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        context.Tenants.Remove(tenant);
        await Task.CompletedTask;
    }
}