namespace Modulus.Core.Money;

using System.Globalization;

/// <summary>
/// An amount of money in one currency (ISO 4217 code, three upper-case letters). Amounts are exact decimals. Arithmetic
/// between different currencies is refused rather than converted: there is no exchange rate in the framework.
/// </summary>
public readonly record struct Money
{
    public Money(decimal amount, string currency)
    {
        if (currency is null || currency.Length != 3 || !currency.All(c => c is >= 'A' and <= 'Z'))
            throw new ArgumentException("A currency is an ISO 4217 code: three upper-case letters (for example EUR).", nameof(currency));

        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Zero(string currency) => new(0m, currency);

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator -(Money money) => new(-money.Amount, money.Currency);

    /// <summary>Multiplies by a factor (a quantity, a rate). The result is not rounded; call <see cref="RoundTo"/>.</summary>
    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    /// <summary>Rounds half away from zero to <paramref name="decimals"/> places (the usual rule for invoices).</summary>
    public Money RoundTo(int decimals) => new(Math.Round(Amount, decimals, MidpointRounding.AwayFromZero), Currency);

    public bool IsZero => Amount == 0m;

    public bool IsNegative => Amount < 0m;

    /// <summary>The number of decimal places the currency uses (its ISO 4217 minor unit; 2 for most currencies).</summary>
    public int MinorUnitDigits => MinorUnits(Currency);

    /// <summary>
    /// Formats the amount with the culture's conventions: the amount is rounded half away from zero to the currency's minor
    /// unit and separators follow the culture. The currency symbol is used only when it is the culture's own currency:
    /// EUR in de-DE gives <c>1.234,56 €</c>, USD in en-US gives <c>$1,234.56</c>. A currency that is not the culture's own
    /// gets its ISO code after the number instead: USD in de-DE gives <c>1.234,56 USD</c>.
    /// </summary>
    public string Format(IFormatProvider culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var digits = MinorUnitDigits;
        var rounded = Math.Round(Amount, digits, MidpointRounding.AwayFromZero);
        var numbers = culture is CultureInfo ci ? (NumberFormatInfo)ci.NumberFormat.Clone() : NumberFormatInfo.GetInstance(culture);

        if (culture is CultureInfo info && OwnsSymbol(info))
        {
            numbers.CurrencyDecimalDigits = digits;
            return rounded.ToString("C", numbers);
        }

        return rounded.ToString("N" + digits, numbers) + "\u00A0" + Currency;
    }

    /// <summary>
    /// Splits the amount in proportion to <paramref name="ratios"/> so that the parts add up to the amount exactly, in minor
    /// units (the usual rule for invoices: no cent is created or lost). The amount is first rounded to the minor unit. Any
    /// leftover minor units go to the first parts, one each.
    /// </summary>
    public IReadOnlyList<Money> Allocate(params int[] ratios)
    {
        ArgumentNullException.ThrowIfNull(ratios);
        if (ratios.Length == 0 || ratios.Any(r => r < 0) || ratios.Sum(r => (long)r) == 0)
            throw new ArgumentException("Allocation needs at least one ratio, none negative, and a positive total.", nameof(ratios));

        var digits = MinorUnitDigits;
        var rounded = Math.Round(Amount, digits, MidpointRounding.AwayFromZero);
        var scale = (decimal)Math.Pow(10, digits);
        var units = decimal.ToInt64(Math.Abs(rounded) * scale);
        var sign = rounded < 0m ? -1m : 1m;
        var total = ratios.Sum(r => (long)r);

        var parts = new List<Money>(ratios.Length);
        var assigned = 0L;
        foreach (var ratio in ratios)
        {
            var share = units * ratio / total;
            assigned += share;
            parts.Add(new Money(share, Currency));
        }

        var leftover = units - assigned;
        for (var i = 0; leftover > 0; i = (i + 1) % parts.Count)
        {
            if (ratios[i] == 0)
                continue;
            parts[i] = new Money(parts[i].Amount + 1, Currency);
            leftover--;
        }

        var currency = Currency;
        return parts.Select(p => new Money(sign * p.Amount / scale, currency)).ToList();
    }

    /// <summary>Returns the ISO 4217 minor-unit digits for a currency code (2 when the code is not in the table).</summary>
    public static int MinorUnits(string currency) => currency switch
    {
        "BIF" or "CLP" or "DJF" or "GNF" or "ISK" or "JPY" or "KMF" or "KRW" or "PYG" or "RWF" or "UGX" or "VND" or "VUV" or "XAF" or "XOF" or "XPF" => 0,
        "BHD" or "IQD" or "JOD" or "KWD" or "LYD" or "OMR" or "TND" => 3,
        "CLF" or "UYW" => 4,
        _ => 2,
    };

    private bool OwnsSymbol(CultureInfo culture)
    {
        try
        {
            return string.Equals(new RegionInfo(culture.Name).ISOCurrencySymbol, Currency, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            // Invariant and neutral cultures have no region.
            return false;
        }
    }

    public override string ToString() => $"{Amount.ToString(CultureInfo.InvariantCulture)} {Currency}";

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            throw new CurrencyMismatchException(left.Currency, right.Currency);
    }
}

/// <summary>Thrown when two amounts in different currencies are combined.</summary>
public sealed class CurrencyMismatchException(string left, string right)
    : InvalidOperationException($"Cannot combine amounts in {left} and {right}: convert them first.")
{
    public string Left { get; } = left;

    public string Right { get; } = right;
}
