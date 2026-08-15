using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Sales.Domain.ValueObjects;

public sealed record OrderStatus
{
    public string Value { get; }

    private static readonly string[] ValidStatuses = 
    {
        "draft", "pending", "confirmed", "processing", 
        "shipped", "delivered", "cancelled", "returned", "refunded"
    };

    private OrderStatus(string value)
    {
        Value = value;
    }

    public static Result<OrderStatus> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<OrderStatus>(Error.Validation("OrderStatus.Empty", "Order status cannot be empty"));
        }

        string normalized = value.ToLowerInvariant().Trim();

        if (!ValidStatuses.Contains(normalized))
        {
            return Result.Failure<OrderStatus>(Error.Validation("OrderStatus.Invalid", "Invalid order status"));
        }

        return Result.Success(new OrderStatus(normalized));
    }

    public static OrderStatus Draft() => new("draft");
    public static OrderStatus Pending() => new("pending");
    public static OrderStatus Confirmed() => new("confirmed");
    public static OrderStatus Processing() => new("processing");
    public static OrderStatus Shipped() => new("shipped");
    public static OrderStatus Delivered() => new("delivered");
    public static OrderStatus Cancelled() => new("cancelled");
    public static OrderStatus Returned() => new("returned");
    public static OrderStatus Refunded() => new("refunded");

    public bool IsFinal => Value == "cancelled" || Value == "delivered" || Value == "returned" || Value == "refunded";
    public bool CanCancel => Value == "draft" || Value == "pending" || Value == "confirmed";
    public bool CanModify => !IsFinal;
    public bool CanShip => Value == "confirmed" || Value == "processing";
}