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

    internal const int MaxNameLength = 100;
    internal const int MaxJobLength = 100;
    internal const int MaxAvatarUrlLength = 2048;
    internal const int MaxEmployeeCodeLength = 32;

    /// <summary>
    /// Checks the details a person may set on their own profile (first and last name, photo address, work e-mail, job title, department).
    /// Returns the normalized values, or the refusal per field. A null input is not checked here; the caller keeps the stored value.
    /// </summary>
    internal static bool TryNormalizeDetails(
        string? firstName, string? lastName, string? avatarUrl, string? workEmail, string? jobTitle, string? department,
        out ProfileDetailValues values, out Dictionary<string, string[]> errors)
    {
        errors = [];
        var first = Trim(firstName);
        var last = Trim(lastName);
        var avatar = Trim(avatarUrl);
        var work = Trim(workEmail);
        var job = Trim(jobTitle);
        var dept = Trim(department);

        if (first is not null && (first.Length > MaxNameLength || HasControl(first)))
            errors["firstName"] = [$"The first name is at most {MaxNameLength} characters and has no control characters."];
        if (last is not null && (last.Length > MaxNameLength || HasControl(last)))
            errors["lastName"] = [$"The last name is at most {MaxNameLength} characters and has no control characters."];
        if (avatar is not null && !IsHttpsUrl(avatar))
            errors["avatarUrl"] = [$"The photo address must be an https address of at most {MaxAvatarUrlLength} characters."];
        if (work is not null && NormalizeEmail(work) is null)
            errors["workEmail"] = ["The work e-mail address is not a usable address."];
        if (job is not null && (job.Length > MaxJobLength || HasControl(job)))
            errors["jobTitle"] = [$"The job title is at most {MaxJobLength} characters and has no control characters."];
        if (dept is not null && (dept.Length > MaxJobLength || HasControl(dept)))
            errors["department"] = [$"The department is at most {MaxJobLength} characters and has no control characters."];

        values = new ProfileDetailValues(first, last, avatar, work, job, dept);
        return errors.Count == 0;
    }

    /// <summary>
    /// Checks an employee code an administrator sets: upper-case letters, digits and hyphens, at most 32 characters. Returns the normalized
    /// code, or null when it is not usable.
    /// </summary>
    internal static string? NormalizeEmployeeCode(string? code)
    {
        var value = Trim(code)?.ToUpperInvariant();
        return value is not null && value.Length <= MaxEmployeeCodeLength && EmployeeCodePattern().IsMatch(value) ? value : null;
    }

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9\-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex EmployeeCodePattern();

    private static bool HasControl(string value) => value.Any(char.IsControl);

    private static bool IsHttpsUrl(string value)
        => value.Length <= MaxAvatarUrlLength && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

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

/// <summary>The normalized profile details, ready to store.</summary>
internal readonly record struct ProfileDetailValues(string? FirstName, string? LastName, string? AvatarUrl, string? WorkEmail, string? JobTitle, string? Department);

/// <summary>The normalized profile values, ready to store.</summary>
internal readonly record struct ProfileValues(string? DisplayName, string? Language, string? TimeZone, string? PhoneNumber);
