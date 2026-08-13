using Microsoft.EntityFrameworkCore;
using ModulusSample.Modules.Partners.Domain.Entities;
using ModulusSample.Modules.Partners.Domain.Repositories;
using ModulusSample.Modules.Partners.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Partners.Infrastructure.Repositories;

public sealed class EfPartnerRepository(PartnersDbContext context) : IPartnerRepository
{
    public async Task<Partner?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await context.Partners
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<PagedResult<Partner>> ListAsync(int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var skip = (page - 1) * pageSize;

        var totalCount = await context.Partners.CountAsync(ct);

        var partners = await context.Partners
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Partner>(partners, totalCount, page, pageSize);
    }

    public async Task AddAsync(Partner partner, CancellationToken ct = default)
    {
        await context.Partners.AddAsync(partner, ct);
    }
}