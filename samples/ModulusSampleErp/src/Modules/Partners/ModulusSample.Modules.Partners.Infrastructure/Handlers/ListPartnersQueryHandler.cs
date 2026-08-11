using Microsoft.EntityFrameworkCore;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Partners.Application.Dtos;
using ModulusSample.Modules.Partners.Application.Queries;
using ModulusSample.Modules.Partners.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Partners.Infrastructure.Handlers;

internal sealed class ListPartnersQueryHandler : IQueryHandler<ListPartnersQuery, PagedResult<PartnerDto>>
{
    private readonly PartnersDbContext _dbContext;

    public ListPartnersQueryHandler(PartnersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<PartnerDto>> HandleAsync(ListPartnersQuery request, CancellationToken cancellationToken)
    {
        var skip = (request.Page - 1) * request.PageSize;

        var totalCount = await _dbContext.Partners.CountAsync(cancellationToken);

        var partners = await _dbContext.Partners
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Skip(skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = partners.Select(p => new PartnerDto(
            p.Id,
            p.Name,
            p.Type,
            p.Email,
            p.Phone,
            p.Address,
            p.OwnerId,
            p.TenantId,
            p.IsActive)).ToList();

        return new PagedResult<PartnerDto>(items, totalCount, request.Page, request.PageSize);
    }
}
