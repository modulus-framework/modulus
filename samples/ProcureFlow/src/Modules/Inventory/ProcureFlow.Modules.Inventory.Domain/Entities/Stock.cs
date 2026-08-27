using Modulus.Core.Abstractions.Domain;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Inventory.Domain.Entities;

public sealed class Stock : AggregateRoot<Guid>
{
    public Guid ProductId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public int QuantityOnHand { get; private set; }
    public int ReorderPoint { get; private set; }
    public int ReorderQuantity { get; private set; }

    public Guid TenantId { get; private set; }

    private Stock() { }

    public static Result<Stock> Create(
        Guid id, Guid productId, Guid warehouseId,
        int quantityOnHand, int reorderPoint, int reorderQuantity, Guid tenantId)
    {
        if (quantityOnHand < 0)
            return Result.Failure<Stock>(Error.Validation("Stock.NegativeQuantity", "Quantity cannot be negative"));
        if (reorderPoint < 0)
            return Result.Failure<Stock>(Error.Validation("Stock.NegativeReorderPoint", "Reorder point cannot be negative"));

        var stock = new Stock
        {
            Id = id,
            ProductId = productId,
            WarehouseId = warehouseId,
            QuantityOnHand = quantityOnHand,
            ReorderPoint = reorderPoint,
            ReorderQuantity = reorderQuantity,
            TenantId = tenantId,
        };

        return Result.Success(stock);
    }

    public Result<bool> Reserve(int quantity)
    {
        if (quantity <= 0)
            return Result.Failure<bool>(Error.Validation("Stock.InvalidQuantity", "Quantity must be positive"));
        if (QuantityOnHand < quantity)
            return Result.Failure<bool>(Error.Validation("Stock.InsufficientStock", "Insufficient stock available"));

        QuantityOnHand -= quantity;
        return Result.Success(true);
    }

    public void Release(int quantity)
    {
        QuantityOnHand += quantity;
    }
}
