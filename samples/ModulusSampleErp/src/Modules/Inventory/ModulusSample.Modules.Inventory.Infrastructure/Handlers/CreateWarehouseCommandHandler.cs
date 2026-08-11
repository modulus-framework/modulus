using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Inventory.Application.Commands;
using ModulusSample.Modules.Inventory.Domain.Entities;
using ModulusSample.Modules.Inventory.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Inventory.Infrastructure.Handlers;

internal sealed class CreateWarehouseCommandHandler : ICommandHandler<CreateWarehouseCommand, Result<Guid>>
{
    private readonly InventoryDbContext _dbContext;
    private readonly ICurrentTenant _currentTenant;

    public CreateWarehouseCommandHandler(
        InventoryDbContext dbContext,
        ICurrentTenant currentTenant)
    {
        _dbContext = dbContext;
        _currentTenant = currentTenant;
    }

    public async Task<Result<Guid>> HandleAsync(CreateWarehouseCommand request, CancellationToken cancellationToken)
    {
        var warehouseId = Guid.NewGuid();
        var tenantId = _currentTenant.TenantId ?? Guid.Empty;

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

        _dbContext.Warehouses.Add(result.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(warehouseId);
    }
}
