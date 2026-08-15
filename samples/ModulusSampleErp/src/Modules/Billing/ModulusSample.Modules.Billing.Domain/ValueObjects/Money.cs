using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Domain.ValueObjects;

public sealed record Money
{
    private static readonly string[] SupportedCurrencies = { "USD", "EUR", "GBP", "CAD", "AUD", "JPY", "CHF" };

    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Result<Money> Create(decimal amount, string currency)
    {
        if (amount < 0)
        {
            return Result.Failure<Money>(Error.Validation("Money.NegativeAmount", "Amount cannot be negative"));
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            return Result.Failure<Money>(Error.Validation("Money.EmptyCurrency", "Currency cannot be empty"));
        }

        string normalizedCurrency = currency.ToUpperInvariant().Trim();

        if (!SupportedCurrencies.Contains(normalizedCurrency))
        {
            return Result.Failure<Money>(Error.Validation("Money.UnsupportedCurrency", $"Currency {currency} is not supported"));
        }

        return Result.Success(new Money(Math.Round(amount, 2), normalizedCurrency));
    }

    public static Money Zero(string currency) => new(0, currency);
    public static Money FromDecimal(decimal amount, string currency) => new(amount, currency);

    public Money Add(Money other)
    {
        if (Currency != other.Currency)
        {
            throw new InvalidOperationException("Cannot add money with different currencies");
        }
        return new Money(Math.Round(Amount + other.Amount, 2), Currency);
    }

    public Money Subtract(Money other)
    {
        if (Currency != other.Currency)
        {
            throw new InvalidOperationException("Cannot subtract money with different currencies");
        }
        return new Money(Math.Round(Amount - other.Amount, 2), Currency);
    }

    public Money Multiply(decimal factor)
    {
        if (factor < 0)
        {
            throw new InvalidOperationException("Cannot multiply money by negative factor");
        }
        return new Money(Math.Round(Amount * factor, 2), Currency);
    }

    public Money Divide(decimal divisor)
    {
        if (divisor <= 0)
        {
            throw new InvalidOperationException("Cannot divide money by zero or negative value");
        }
        return new Money(Math.Round(Amount / divisor, 2), Currency);
    }

    public bool GreaterThan(Money other) => Currency == other.Currency && Amount > other.Amount;
    public bool LessThan(Money other) => Currency == other.Currency && Amount < other.Amount;
    public bool GreaterThanOrEqual(Money other) => Currency == other.Currency && Amount >= other.Amount;
    public bool LessThanOrEqual(Money other) => Currency == other.Currency && Amount <= other.Amount;

    public override string ToString() => $"{Amount:N2} {Currency}";
}