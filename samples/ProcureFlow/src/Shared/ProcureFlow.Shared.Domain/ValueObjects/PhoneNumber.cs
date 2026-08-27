using System.Text.RegularExpressions;

using ProcureFlow.Shared.Domain;
namespace ProcureFlow.Shared.Domain.ValueObjects;

public sealed record PhoneNumber
{
    private static readonly Regex PhoneRegex = new(
        @"^\+?[1-9]\d{1,14}$",
        RegexOptions.Compiled);

    public string Value { get; init; }
    public string? CountryCode { get; init; }

    private PhoneNumber(string value, string? countryCode = null)
    {
        Value = value;
        CountryCode = countryCode;
    }

    internal static PhoneNumber Reconstitute(string value) => new(value);

    public static Result<PhoneNumber> Create(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return Result.Failure<PhoneNumber>(Error.Validation(
                "PhoneNumber.Empty",
                "Phone number is required"));
        }

        // Remove common formatting characters: spaces, dashes, parentheses, dots
        phoneNumber = phoneNumber.Trim().Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "").Replace(".", "");

        if (!PhoneRegex.IsMatch(phoneNumber))
        {
            return Result.Failure<PhoneNumber>(Error.Validation(
                "PhoneNumber.InvalidFormat",
                "Phone number format is invalid"));
        }

        string? countryCode = null;
        if (phoneNumber.StartsWith('+'))
        {
            countryCode = phoneNumber[..3];
        }

        return new PhoneNumber(phoneNumber, countryCode);
    }

    public override string ToString() => Value;
}
