using System.Text.RegularExpressions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Domain.ValueObjects;

public sealed record InvoiceNumber
{
    private static readonly Regex Regex = new(@"^INV-\d{4}-\d{6}$", RegexOptions.Compiled);

    public string Value { get; }

    private InvoiceNumber(string value)
    {
        Value = value;
    }

    public static Result<InvoiceNumber> Create(int year, int sequenceNumber)
    {
        if (year < 2000 || year > 2100)
        {
            return Result.Failure<InvoiceNumber>(Error.Validation("InvoiceNumber.InvalidYear", "Year must be between 2000 and 2100"));
        }

        if (sequenceNumber < 1 || sequenceNumber > 999999)
        {
            return Result.Failure<InvoiceNumber>(Error.Validation("InvoiceNumber.InvalidSequence", "Sequence number must be between 1 and 999999"));
        }

        string value = $"INV-{year}-{sequenceNumber:D6}";
        return Result.Success(new InvoiceNumber(value));
    }

    public static Result<InvoiceNumber> FromString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<InvoiceNumber>(Error.Validation("InvoiceNumber.Empty", "Invoice number cannot be empty"));
        }

        if (!Regex.IsMatch(value))
        {
            return Result.Failure<InvoiceNumber>(Error.Validation("InvoiceNumber.InvalidFormat", "Invoice number must be in format INV-YYYY-NNNNNN"));
        }

        return Result.Success(new InvoiceNumber(value));
    }

    public static InvoiceNumber FromStringUnsafe(string value) => new(value);
}