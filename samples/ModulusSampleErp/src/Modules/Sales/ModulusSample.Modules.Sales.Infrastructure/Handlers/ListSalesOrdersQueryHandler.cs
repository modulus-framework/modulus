using Microsoft.EntityFrameworkCore;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Sales.Application.Dtos;
using ModulusSample.Modules.Sales.Application.Queries;
using ModulusSample.Modules.Sales.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Sales.Infrastructure.Handlers;

internal sealed class ListSalesOrdersQueryHandler : IQueryHandler<ListSalesOrdersQuery, PagedResult<SalesOrderDto>>
{
    private readonly SalesDbContext _dbContext;

    public ListSalesOrdersQueryHandler(SalesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<SalesOrderDto>> HandleAsync(ListSalesOrdersQuery request, CancellationToken cancellationToken)
    {
        var skip = (request.Page - 1) * request.PageSize;

        var totalCount = await _dbContext.Orders.CountAsync(cancellationToken);

        var orders = await _dbContext.Orders
            .AsNoTracking()
            .OrderByDescending(o => o.Id)
            .Skip(skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = orders.Select(o => new SalesOrderDto(
            o.Id,
            o.OrderNumber,
            o.CustomerId,
            o.Status,
            o.TotalAmount,
            o.OrgUnitId,
            o.TenantId)).ToList();

        return new PagedResult<SalesOrderDto>(items, totalCount, request.Page, request.PageSize);
    }
}
