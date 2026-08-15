using System.Text.RegularExpressions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Inventory.Domain.ValueObjects;

public sealed record LocationCode
{
    private static readonly Regex Regex = new(@"^[A-Z]{2}-[A-Z0-9]{4}-[A-Z0-9]{4}$", RegexOptions.Compiled);

    public string Value { get; }

    private LocationCode(string value)
    {
        Value = value.ToUpperInvariant().Trim();
    }

    public static Result<LocationCode> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<LocationCode>(Error.Validation("LocationCode.Empty", "Location code cannot be empty"));
        }

        string normalized = value.ToUpperInvariant().Trim();

        if (!Regex.IsMatch(normalized))
        {
            return Result.Failure<LocationCode>(Error.Validation("LocationCode.InvalidFormat", "Location code must be in format XX-XXXX-XXXX"));
        }

        return Result.Success(new LocationCode(normalized));
    }

    public static LocationCode FromString(string value) => new(value);
    public static LocationCode FromParts(string zone, string aisle, string rack) => new($"{zone}-{aisle}-{rack}");
}