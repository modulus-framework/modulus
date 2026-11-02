namespace Modulus.Core.Tests;

using FluentAssertions;
using Modulus.Core.Money;
using Xunit;

[Trait("Category", "Unit")]
public sealed class MoneyTests
{
    [Fact]
    public void Adds_and_subtracts_amounts_in_the_same_currency_exactly()
    {
        var total = new Money(10.10m, "EUR") + new Money(0.20m, "EUR");

        total.Should().Be(new Money(10.30m, "EUR"));
        (total - new Money(10.30m, "EUR")).IsZero.Should().BeTrue();
    }

    [Fact]
    public void Refuses_to_combine_two_currencies()
    {
        var act = () => _ = new Money(1m, "EUR") + new Money(1m, "USD");

        act.Should().Throw<CurrencyMismatchException>()
           .Where(e => e.Left == "EUR" && e.Right == "USD");
    }

    [Theory]
    [InlineData("eur")]
    [InlineData("EURO")]
    [InlineData("E1R")]
    [InlineData("")]
    public void Rejects_a_currency_that_is_not_an_iso_code(string currency)
    {
        var act = () => new Money(1m, currency);

        act.Should().Throw<ArgumentException>().WithParameterName("currency");
    }

    [Fact]
    public void Rounds_half_away_from_zero_for_invoices()
    {
        new Money(2.345m, "EUR").RoundTo(2).Should().Be(new Money(2.35m, "EUR"));
        new Money(-2.345m, "EUR").RoundTo(2).Should().Be(new Money(-2.35m, "EUR"));
    }

    [Fact]
    public void Multiplies_by_a_quantity_and_reports_sign_and_zero()
    {
        var line = new Money(4.50m, "GBP").Multiply(3m);

        line.Amount.Should().Be(13.50m);
        line.IsNegative.Should().BeFalse();
        (-line).IsNegative.Should().BeTrue();
        Money.Zero("GBP").IsZero.Should().BeTrue();
    }

    [Fact]
    public void Prints_the_amount_with_a_period_and_the_currency()
    {
        new Money(1234.5m, "JPY").ToString().Should().Be("1234.5 JPY");
    }

    [Theory]
    [InlineData("JPY", 0)]
    [InlineData("EUR", 2)]
    [InlineData("KWD", 3)]
    [InlineData("XXX-UNKNOWN", 2)]
    public void Minor_unit_digits_follow_iso_4217(string currency, int digits)
    {
        Money.MinorUnits(currency).Should().Be(digits);
    }

    [Fact]
    public void Formats_with_the_culture_and_its_own_currency_symbol()
    {
        new Money(1234.5m, "EUR").Format(System.Globalization.CultureInfo.GetCultureInfo("de-DE")).Should().Be("1.234,50 €");
        new Money(1234.5m, "USD").Format(System.Globalization.CultureInfo.GetCultureInfo("en-US")).Should().Be("$1,234.50");
    }

    [Fact]
    public void Formats_a_foreign_currency_with_its_iso_code()
    {
        var text = new Money(1234.5m, "USD").Format(System.Globalization.CultureInfo.GetCultureInfo("de-DE"));

        text.Should().Be("1.234,50\u00A0USD");
    }

    [Fact]
    public void Formats_zero_decimal_currencies_without_fraction()
    {
        new Money(1234.4m, "JPY").Format(System.Globalization.CultureInfo.GetCultureInfo("ja-JP")).Should().Be("￥1,234");
    }

    [Fact]
    public void Allocation_adds_up_exactly_and_gives_leftover_cents_to_the_first_parts()
    {
        var parts = new Money(100.00m, "EUR").Allocate(1, 1, 1);

        parts.Select(p => p.Amount).Should().Equal(33.34m, 33.33m, 33.33m);
        parts.Aggregate(Money.Zero("EUR"), (sum, p) => sum + p).Should().Be(new Money(100.00m, "EUR"));
    }

    [Fact]
    public void Allocation_is_proportional_and_keeps_negative_sign()
    {
        var parts = new Money(-10.00m, "EUR").Allocate(3, 1);

        parts.Select(p => p.Amount).Should().Equal(-7.50m, -2.50m);
    }

    [Fact]
    public void Allocation_refuses_empty_or_zero_ratios()
    {
        var act = () => new Money(1m, "EUR").Allocate(0, 0);

        act.Should().Throw<ArgumentException>();
    }
}
