using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Catalog.Domain.ValueObjects;

public sealed record Price
{
    private static readonly string[] SupportedCurrencies = { "USD", "EUR", "GBP", "CAD", "AUD", "JPY", "CHF" };

    public decimal Amount { get; }
    public string Currency { get; }

    private Price(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Result<Price> Create(decimal amount, string currency)
    {
        if (amount <= 0)
        {
            return Result.Failure<Price>(Error.Validation("Price.InvalidAmount", "Price must be greater than zero"));
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            return Result.Failure<Price>(Error.Validation("Price.EmptyCurrency", "Currency cannot be empty"));
        }

        string normalizedCurrency = currency.ToUpperInvariant().Trim();

        if (!SupportedCurrencies.Contains(normalizedCurrency))
        {
            return Result.Failure<Price>(Error.Validation("Price.UnsupportedCurrency", $"Currency {currency} is not supported"));
        }

        return Result.Success(new Price(Math.Round(amount, 2), normalizedCurrency));
    }

    public static Price FromDecimal(decimal amount, string currency) => new(amount, currency);

    public Price WithDiscount(decimal percentage)
    {
        if (percentage < 0 || percentage > 100)
        {
            throw new ArgumentException("Discount percentage must be between 0 and 100");
        }

        decimal discountAmount = Math.Round(Amount * percentage / 100, 2);
        decimal newAmount = Math.Max(Math.Round(Amount - discountAmount, 2), 0.01m);
        return new Price(newAmount, Currency);
    }

    public Price WithMarkup(decimal percentage)
    {
        if (percentage < 0)
        {
            throw new ArgumentException("Markup percentage cannot be negative");
        }

        decimal markupAmount = Math.Round(Amount * percentage / 100, 2);
        return new Price(Math.Round(Amount + markupAmount, 2), Currency);
    }

    public bool GreaterThan(Price other) => Currency == other.Currency && Amount > other.Amount;
    public bool LessThan(Price other) => Currency == other.Currency && Amount < other.Amount;
    public bool GreaterThanOrEqual(Price other) => Currency == other.Currency && Amount >= other.Amount;
    public bool LessThanOrEqual(Price other) => Currency == other.Currency && Amount <= other.Amount;

    public override string ToString() => $"{Amount:N2} {Currency}";
}