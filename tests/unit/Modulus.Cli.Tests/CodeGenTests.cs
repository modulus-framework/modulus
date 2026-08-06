using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public class CodeGenTests
{
    // ── Pluralize ──────────────────────────────────────────────────

    [Theory]
    [InlineData("Product", "Products")]
    [InlineData("Order", "Orders")]
    [InlineData("Box", "Boxes")]
    [InlineData("Category", "Categories")]
    [InlineData("Entity", "Entities")]
    [InlineData("Status", "Statuses")]
    [InlineData("Bus", "Buses")]
    [InlineData("Glass", "Glasses")]
    [InlineData("Switch", "Switches")]
    [InlineData("Wish", "Wishes")]
    [InlineData("Day", "Days")]          // vowel+y → just +s
    [InlineData("Key", "Keys")]          // vowel+y → just +s
    [InlineData("Boy", "Boys")]          // vowel+y → just +s
    public void Pluralize_returns_expected_plural(string singular, string expected)
    {
        CodeGen.Pluralize(singular).Should().Be(expected);
    }

    [Fact]
    public void Pluralize_handles_empty_string()
    {
        CodeGen.Pluralize("").Should().Be("");
    }

    // ── ToCamelCase ────────────────────────────────────────────────

    [Theory]
    [InlineData("ProductName", "productName")]
    [InlineData("Order", "order")]
    [InlineData("MyEntity", "myEntity")]
    [InlineData("A", "a")]
    public void ToCamelCase_lowercases_first_char(string input, string expected)
    {
        CodeGen.ToCamelCase(input).Should().Be(expected);
    }

    // ── ValidateIdentifier ─────────────────────────────────────────

    [Theory]
    [InlineData("Product")]
    [InlineData("OrderDetail")]
    [InlineData("My_Entity")]
    [InlineData("_private")]
    public void ValidateIdentifier_accepts_valid_names(string name)
    {
        var result = CodeGen.ValidateIdentifier(name, "Entity");
        result.Should().Be(name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateIdentifier_rejects_empty(string name)
    {
        var act = () => CodeGen.ValidateIdentifier(name, "Entity");
        act.Should().Throw<ArgumentException>()
           .WithMessage("*Entity name cannot be empty*");
    }

    [Theory]
    [InlineData("123Entity")]
    [InlineData("2Way")]
    public void ValidateIdentifier_rejects_names_starting_with_digit(string name)
    {
        // The regex check fires first (digits aren't valid identifier starts),
        // so the error is "not a valid C# identifier", not "must not start with a digit".
        var act = () => CodeGen.ValidateIdentifier(name, "Entity");
        act.Should().Throw<ArgumentException>()
           .WithMessage("*not a valid C# identifier*");
    }

    [Theory]
    [InlineData("My Entity")]
    [InlineData("My-Entity")]
    [InlineData("My.Entity")]
    [InlineData("Entity!")]
    public void ValidateIdentifier_rejects_special_characters(string name)
    {
        var act = () => CodeGen.ValidateIdentifier(name, "Entity");
        act.Should().Throw<ArgumentException>()
           .WithMessage("*not a valid C# identifier*");
    }
}
