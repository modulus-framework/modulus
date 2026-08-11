using Microsoft.EntityFrameworkCore;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Sales.Application.Dtos;
using ModulusSample.Modules.Sales.Application.Queries;
using ModulusSample.Modules.Sales.Infrastructure.Database;

namespace ModulusSample.Modules.Sales.Infrastructure.Handlers;

internal sealed class GetSalesOrderByIdQueryHandler : IQueryHandler<GetSalesOrderByIdQuery, SalesOrderDto?>
{
    private readonly SalesDbContext _dbContext;

    public GetSalesOrderByIdQueryHandler(SalesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SalesOrderDto?> HandleAsync(GetSalesOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);

        if (order is null)
            return null;

        return new SalesOrderDto(
            order.Id,
            order.OrderNumber,
            order.CustomerId,
            order.Status,
            order.TotalAmount,
            order.OrgUnitId,
            order.TenantId);
    }
}
