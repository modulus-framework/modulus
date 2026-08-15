using Modulus.Core.Abstractions.Domain;
using Modulus.Core.Abstractions.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Sales.Domain.Entities;

public sealed class SalesOrder : AggregateRoot<Guid>, IHasOrgUnit
{
    public string OrderNumber { get; private set; } = null!;
    public Guid CustomerId { get; private set; }
    public string Status { get; private set; } = "Draft";
    public decimal TotalAmount { get; private set; }

    public Guid OrgUnitId { get; private set; }
    public Guid TenantId { get; private set; }

    private readonly List<OrderLine> _lines = [];

    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();

    private SalesOrder() { }

    public static Result<SalesOrder> Create(
        Guid id, string orderNumber, Guid customerId, Guid orgUnitId, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            return Result.Failure<SalesOrder>(Error.Validation("SalesOrder.OrderNumberRequired", "Order number is required"));

        var order = new SalesOrder
        {
            Id = id,
            OrderNumber = orderNumber,
            CustomerId = customerId,
            OrgUnitId = orgUnitId,
            TenantId = tenantId,
            Status = "Draft",
            TotalAmount = 0m,
        };

        return Result.Success(order);
    }

    public Result<bool> AddLine(Guid productId, int quantity, decimal unitPrice)
    {
        if (quantity <= 0)
            return Result.Failure<bool>(Error.Validation("OrderLine.QuantityInvalid", "Quantity must be positive"));
        if (unitPrice < 0)
            return Result.Failure<bool>(Error.Validation("OrderLine.NegativePrice", "Price cannot be negative"));

        var line = new OrderLine(Guid.NewGuid(), productId, quantity, unitPrice);
        _lines.Add(line);
        TotalAmount += quantity * unitPrice;

        return Result.Success(true);
    }

    public void Confirm()
    {
        Status = "Confirmed";
    }

    public void Cancel()
    {
        Status = "Cancelled";
    }
}

public sealed record OrderLine(
    Guid Id,
    Guid ProductId,
    int Quantity,
    decimal UnitPrice)
{
    public decimal LineTotal => Quantity * UnitPrice;
}
