namespace ProcureFlow.Shared.Application.Abstractions.Oidc;

/// <summary>
/// Represents user information extracted from OIDC ID token
/// </summary>
public sealed record OidcUserInfo(
    string Subject,
    string Email,
    string UserName,
    string FirstName,
    string LastName,
    string? EmailVerifiedValue = null,
    string? PhoneNumber = null,
    bool? PhoneNumberVerified = null,
    string? Picture = null,
    string? Locale = null,
    string? ZoneInfo = null,
    DateTime? UpdatedAt = null)
{
    /// <summary>
    /// Gets the subject identifier (unique user ID from Keycloak)
    /// </summary>
    public string Subject { get; } = Subject;

    /// <summary>
    /// Gets the user's email address
    /// </summary>
    public string Email { get; } = Email;

    /// <summary>
    /// Gets the username
    /// </summary>
    public string UserName { get; } = UserName;

    /// <summary>
    /// Gets the user's first name
    /// </summary>
    public string FirstName { get; } = FirstName;

    /// <summary>
    /// Gets the user's last name
    /// </summary>
    public string LastName { get; } = LastName;

    /// <summary>
    /// Gets whether the email is verified
    /// </summary>
    public bool EmailVerified => EmailVerifiedValue?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

    /// <summary>
    /// Gets the user's phone number (optional)
    /// </summary>
    public string? PhoneNumber { get; } = PhoneNumber;

    /// <summary>
    /// Gets whether the phone number is verified (optional)
    /// </summary>
    public bool? PhoneNumberVerified { get; } = PhoneNumberVerified;

    /// <summary>
    /// Gets the profile picture URL (optional)
    /// </summary>
    public string? Picture { get; } = Picture;

    /// <summary>
    /// Gets the user's locale (optional)
    /// </summary>
    public string? Locale { get; } = Locale;

    /// <summary>
    /// Gets the user's time zone (optional)
    /// </summary>
    public string? ZoneInfo { get; } = ZoneInfo;

    /// <summary>
    /// Gets the last update time (optional)
    /// </summary>
    public DateTime? UpdatedAt { get; } = UpdatedAt;

    /// <summary>
    /// Gets the user's full name
    /// </summary>
    public string FullName => $"{FirstName} {LastName}".Trim();
}
