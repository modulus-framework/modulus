using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Inventory.Domain.Entities;
using ModulusSample.Modules.Inventory.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Inventory.Application.Warehouses.Commands;

public sealed class CreateWarehouseCommandHandler(
    IWarehouseRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateWarehouseCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateWarehouseCommand request, CancellationToken cancellationToken)
    {
        var warehouseId = Guid.NewGuid();
        var tenantId = currentTenant.TenantId ?? Guid.Empty;

        var result = Warehouse.Create(
            warehouseId,
            request.Code,
            request.Name,
            request.Address,
            request.City,
            request.PostalCode,
            request.Country,
            request.OrgUnitId,
            tenantId);

        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        await repository.AddAsync(result.Value, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return Result.Success(warehouseId);
    }
}