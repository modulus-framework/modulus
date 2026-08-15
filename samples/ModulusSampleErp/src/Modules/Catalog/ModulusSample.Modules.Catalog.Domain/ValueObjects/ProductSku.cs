using System.Text.RegularExpressions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Catalog.Domain.ValueObjects;

public sealed record ProductSku
{
    private static readonly Regex Regex = new(@"^[A-Z]{3}-\d{4}-[A-Z0-9]{6}$", RegexOptions.Compiled);

    public string Value { get; }

    private ProductSku(string value)
    {
        Value = value.ToUpperInvariant().Trim();
    }

    public static Result<ProductSku> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<ProductSku>(Error.Validation("ProductSku.Empty", "SKU cannot be empty"));
        }

        string normalized = value.ToUpperInvariant().Trim();

        if (!Regex.IsMatch(normalized))
        {
            return Result.Failure<ProductSku>(Error.Validation("ProductSku.InvalidFormat", "SKU must be in format XXX-NNNN-XXXXXX"));
        }

        return Result.Success(new ProductSku(normalized));
    }

    public static ProductSku FromString(string value) => new(value);
    public static ProductSku FromParts(string prefix, int sequence, string suffix) => new($"{prefix}-{sequence:D4}-{suffix}");
}