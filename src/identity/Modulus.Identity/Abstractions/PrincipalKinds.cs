using System.Text.RegularExpressions;

namespace Modulus.Identity.Abstractions;

/// <summary>
/// What kind of principal an identity is (A1). The framework names only the two kinds every application needs; an application adds its own
/// (for example <c>employee</c>, <c>contractor</c>) as data, with no framework change. A human kind carries employment or contact attributes;
/// a non-human kind carries none, so a service account is never forced to supply human fields. System jobs are not stored principals: they
/// run under a named identity carried in the job envelope (see the plan's A6).
/// </summary>
public static class PrincipalKinds
{
    /// <summary>A human principal. The default for an account that does not say otherwise.</summary>
    public const string Person = "person";

    /// <summary>A non-human principal, such as an integration. Carries no human attributes.</summary>
    public const string ServiceAccount = "service-account";

    private static readonly Regex Valid = new("^[a-z][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant);

    /// <summary>The kind in canonical form: lower-case, starting with a letter, letters digits and hyphens only.</summary>
    /// <exception cref="ArgumentException">The kind is not a valid name.</exception>
    public static string Normalize(string kind)
    {
        var value = kind?.Trim().ToLowerInvariant() ?? string.Empty;
        return Valid.IsMatch(value)
            ? value
            : throw new ArgumentException("A principal kind is lower-case letters, digits and hyphens, starting with a letter, up to 32 characters.", nameof(kind));
    }
}

/// <summary>
/// Why an account is administratively suspended (ADR-02, the restriction dimension). The framework names the common reasons; an application
/// may add its own codes. A suspension is separate from the lifecycle and from lockout: it keeps the account's history and grants, and the
/// reason is recorded with it.
/// </summary>
public static class SuspensionReasons
{
    /// <summary>The account is held while a security concern is reviewed.</summary>
    public const string SecurityReview = "security-review";

    /// <summary>The account broke a policy; the review decides whether it returns.</summary>
    public const string PolicyViolation = "policy-violation";

    /// <summary>The person is away for a planned period; the account returns when it ends.</summary>
    public const string Absence = "absence";

    /// <summary>An administrator holds the account for a reason outside the other codes. A note is required.</summary>
    public const string AdministrativeHold = "administrative-hold";

    /// <summary>Any other reason. A note is required.</summary>
    public const string Other = "other";

    /// <summary>Whether a suspension with this code must carry a free-text note explaining it.</summary>
    public static bool RequiresNote(string code)
        => string.Equals(code, AdministrativeHold, StringComparison.Ordinal) || string.Equals(code, Other, StringComparison.Ordinal);
}
