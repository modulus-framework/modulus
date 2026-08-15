using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Inventory.Domain.ValueObjects;

public sealed record StockQuantity
{
    public int Value { get; }

    private StockQuantity(int value)
    {
        Value = value;
    }

    public static Result<StockQuantity> Create(int value)
    {
        if (value < 0)
        {
            return Result.Failure<StockQuantity>(Error.Validation("StockQuantity.Negative", "Stock quantity cannot be negative"));
        }

        return Result.Success(new StockQuantity(value));
    }

    public static StockQuantity Zero() => new(0);
    public static StockQuantity FromInt(int value) => new(value);

    public StockQuantity Add(int amount) => new(Math.Max(0, Value + amount));
    public StockQuantity Subtract(int amount) => new(Math.Max(0, Value - amount));

    public bool IsAvailable(int requiredQuantity) => Value >= requiredQuantity;
    public bool IsLowStock(int threshold) => Value <= threshold;
    public bool IsOutOfStock => Value == 0;

    public override string ToString() => Value.ToString();
}