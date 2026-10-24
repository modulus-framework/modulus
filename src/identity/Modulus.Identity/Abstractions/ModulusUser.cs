using Microsoft.AspNetCore.Identity;

namespace Modulus.Identity.Abstractions;

/// <summary>
/// Base user entity for all Modulus applications.
/// Extends ASP.NET Identity with tenant support and profile fields.
/// </summary>
public class ModulusUser : IdentityUser<Guid>
{
    public Guid? TenantId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The name the person chose for themselves (self-service profile, <c>PUT account/profile</c>). Null when not set. Needs a column in
    /// apps that already have users.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>The person's language as a BCP 47 tag (for example <c>en-GB</c>). Null when not set. Needs a column in apps that already have users.</summary>
    public string? Language { get; set; }

    /// <summary>The person's time zone as an IANA or Windows id (for example <c>Europe/London</c>). Null when not set. Needs a column in apps that already have users.</summary>
    public string? TimeZone { get; set; }

    /// <summary>
    /// Shop-floor employee code, upper-case, unique within the company (checked when it is set). Null for an ordinary account.
    /// Needs a column in apps that already have users.
    /// </summary>
    public string? EmployeeCode { get; set; }

    /// <summary>Hash of the shop-floor PIN (the app's password hasher); null when the account has no PIN.</summary>
    public string? PinHash { get; set; }

    /// <summary>
    /// The PIN was set by someone else (an import, a reset): the operator must choose their own at the next sign-in before a
    /// token is issued. Needs a column in apps that already have users.
    /// </summary>
    public bool MustChangePin { get; set; }

    /// <summary>
    /// The identifier the provisioning system (SCIM client, HR system) uses for this person. Null for an account it did not
    /// create. Needs a column in apps that already have users.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// What kind of principal the account is (<see cref="PrincipalKinds"/>, an open vocabulary). Needs a column in apps that already have
    /// users; existing rows read as <see cref="PrincipalKinds.Person"/>.
    /// </summary>
    public string PrincipalKind { get; set; } = PrincipalKinds.Person;

    /// <summary>
    /// Where the account is in its life (<see cref="AccountLifecycle"/>). Changed only through <see cref="AccountLifecycleRules"/>. Needs a
    /// column in apps that already have users; existing rows read as <see cref="AccountLifecycle.Active"/>.
    /// </summary>
    public AccountLifecycle Lifecycle { get; set; } = AccountLifecycle.Active;

    /// <summary>When the account was created through SCIM (<c>meta.created</c>); null for accounts created another way.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>When the account last changed through SCIM (<c>meta.lastModified</c>); null when SCIM never changed it.</summary>
    public DateTimeOffset? ModifiedAt { get; set; }


    /// <summary>When <see cref="Lifecycle"/> last changed; null for an account that never moved.</summary>
    public DateTimeOffset? LifecycleChangedAt { get; set; }

    /// <summary>
    /// Why the account is administratively suspended (<see cref="SuspensionReasons"/>, an open vocabulary); null when it is not.
    /// Independent of lock-out and of <see cref="Lifecycle"/>.
    /// </summary>
    public string? SuspensionReason { get; set; }

    /// <summary>The note that goes with the suspension (required by <see cref="SuspensionReasons.RequiresNote"/> reasons).</summary>
    public string? SuspensionNote { get; set; }

    public string FullName =>
        string.IsNullOrWhiteSpace(FirstName) && string.IsNullOrWhiteSpace(LastName)
            ? UserName ?? Email ?? "Unknown"
            : $"{FirstName} {LastName}".Trim();
}
