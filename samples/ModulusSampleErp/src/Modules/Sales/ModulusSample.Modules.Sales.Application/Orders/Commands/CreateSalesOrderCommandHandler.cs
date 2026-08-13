using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Sales.Domain.Entities;
using ModulusSample.Modules.Sales.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Sales.Application.Orders.Commands;

public sealed class CreateSalesOrderCommandHandler(
    ISalesOrderRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateSalesOrderCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateSalesOrderCommand request, CancellationToken cancellationToken)
    {
        var orderId = Guid.NewGuid();
        var tenantId = currentTenant.TenantId ?? Guid.Empty;

        var result = SalesOrder.Create(
            orderId,
            request.OrderNumber,
            request.CustomerId,
            request.OrgUnitId,
            tenantId);

        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        await repository.AddAsync(result.Value, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return Result.Success(orderId);
    }
}