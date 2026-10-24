namespace Modulus.Identity;

using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// Validation and normalization of the self-service profile (<c>account/profile</c>). Values the person may not choose are refused
/// with a message per field; an empty optional field is stored as null.
/// </summary>
internal static partial class ProfileRules
{
    internal const int MaxDisplayNameLength = 100;
    internal const int MaxLanguageLength = 35;
    internal const int MaxTimeZoneLength = 100;
    internal const int MaxPhoneNumberLength = 32;
    internal const int MaxEmailLength = 256;

    [GeneratedRegex(@"^\+?[0-9 ()\-]{6,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    /// <summary>Checks a profile update. Returns the normalized values, or the refusal per field.</summary>
    internal static bool TryNormalize(
        string? displayName, string? language, string? timeZone, string? phoneNumber,
        out ProfileValues values, out Dictionary<string, string[]> errors)
    {
        errors = [];
        var name = Trim(displayName);
        var lang = Trim(language);
        var zone = Trim(timeZone);
        var phone = Trim(phoneNumber);

        if (name is not null && name.Length > MaxDisplayNameLength)
            errors["displayName"] = [$"The display name is at most {MaxDisplayNameLength} characters."];

        if (lang is not null && (lang.Length > MaxLanguageLength || !IsLanguageTag(lang)))
            errors["language"] = ["The language must be a BCP 47 tag, for example en-GB."];

        if (zone is not null && (zone.Length > MaxTimeZoneLength || !IsTimeZone(zone)))
            errors["timeZone"] = ["The time zone must be a known IANA or Windows id, for example Europe/London."];

        if (phone is not null && !PhonePattern().IsMatch(phone))
            errors["phoneNumber"] = ["The phone number may contain digits, spaces, brackets, hyphens and a leading plus, 6 to 32 characters."];

        values = new ProfileValues(name, lang, zone, phone);
        return errors.Count == 0;
    }

    /// <summary>Checks an address for an email change. Returns the trimmed address, or null when it is not a usable address.</summary>
    internal static string? NormalizeEmail(string? email)
    {
        var address = Trim(email);
        if (address is null || address.Length > MaxEmailLength)
            return null;
        try
        {
            var parsed = new System.Net.Mail.MailAddress(address);
            return parsed.Address == address ? address : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsLanguageTag(string tag)
    {
        try
        {
            // A neutral or specific culture: GetCultureInfo accepts the BCP 47 forms and throws for an unknown tag.
            _ = CultureInfo.GetCultureInfo(tag);
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static bool IsTimeZone(string id)
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}

/// <summary>The normalized profile values, ready to store.</summary>
internal readonly record struct ProfileValues(string? DisplayName, string? Language, string? TimeZone, string? PhoneNumber);
