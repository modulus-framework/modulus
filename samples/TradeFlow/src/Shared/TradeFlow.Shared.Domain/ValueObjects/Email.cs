using System.Text.RegularExpressions;

using TradeFlow.Shared.Domain;
namespace TradeFlow.Shared.Domain.ValueObjects;

public sealed record Email
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Value { get; init; }

    private Email(string value) => Value = value;

    public static Result<Email> Create(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Result.Failure<Email>(Error.Validation(
                "Email.Empty",
                "Email address is required"));
        }

        email = email.Trim().ToLowerInvariant();

        if (!EmailRegex.IsMatch(email))
        {
            return Result.Failure<Email>(Error.Validation(
                "Email.InvalidFormat",
                "Email address format is invalid"));
        }

        return new Email(email);
    }

    public override string ToString() => Value;
}
