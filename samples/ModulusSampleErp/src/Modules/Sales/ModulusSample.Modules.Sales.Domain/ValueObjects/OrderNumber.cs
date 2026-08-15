using System.Text.RegularExpressions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Sales.Domain.ValueObjects;

public sealed record OrderNumber
{
    private static readonly Regex Regex = new(@"^ORD-\d{4}-\d{6}$", RegexOptions.Compiled);

    public string Value { get; }

    private OrderNumber(string value)
    {
        Value = value;
    }

    public static Result<OrderNumber> Create(int year, int sequenceNumber)
    {
        if (year < 2000 || year > 2100)
        {
            return Result.Failure<OrderNumber>(Error.Validation("OrderNumber.InvalidYear", "Year must be between 2000 and 2100"));
        }

        if (sequenceNumber < 1 || sequenceNumber > 999999)
        {
            return Result.Failure<OrderNumber>(Error.Validation("OrderNumber.InvalidSequence", "Sequence number must be between 1 and 999999"));
        }

        string value = $"ORD-{year}-{sequenceNumber:D6}";
        return Result.Success(new OrderNumber(value));
    }

    public static Result<OrderNumber> FromString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<OrderNumber>(Error.Validation("OrderNumber.Empty", "Order number cannot be empty"));
        }

        if (!Regex.IsMatch(value))
        {
            return Result.Failure<OrderNumber>(Error.Validation("OrderNumber.InvalidFormat", "Order number must be in format ORD-YYYY-NNNNNN"));
        }

        return Result.Success(new OrderNumber(value));
    }

    public static OrderNumber FromStringUnsafe(string value) => new(value);
}