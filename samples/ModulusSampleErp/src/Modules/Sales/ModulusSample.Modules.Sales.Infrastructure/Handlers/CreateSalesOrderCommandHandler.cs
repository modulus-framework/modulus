using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Sales.Application.Commands;
using ModulusSample.Modules.Sales.Domain.Entities;
using ModulusSample.Modules.Sales.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Sales.Infrastructure.Handlers;

internal sealed class CreateSalesOrderCommandHandler : ICommandHandler<CreateSalesOrderCommand, Result<Guid>>
{
    private readonly SalesDbContext _dbContext;
    private readonly ICurrentTenant _currentTenant;

    public CreateSalesOrderCommandHandler(
        SalesDbContext dbContext,
        ICurrentTenant currentTenant)
    {
        _dbContext = dbContext;
        _currentTenant = currentTenant;
    }

    public async Task<Result<Guid>> HandleAsync(CreateSalesOrderCommand request, CancellationToken cancellationToken)
    {
        var orderId = Guid.NewGuid();
        var tenantId = _currentTenant.TenantId ?? Guid.Empty;

        var result = SalesOrder.Create(
            orderId,
            request.OrderNumber,
            request.CustomerId,
            request.OrgUnitId,
            tenantId);

        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        _dbContext.Orders.Add(result.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(orderId);
    }
}
