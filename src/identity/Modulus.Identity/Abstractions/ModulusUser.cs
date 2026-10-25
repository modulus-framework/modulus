using Microsoft.AspNetCore.Identity;
using Modulus.Core.Abstractions.Entities;

namespace Modulus.Identity.Abstractions;

/// <summary>
/// Base user entity for all Modulus applications.
/// Extends ASP.NET Identity with tenant support and profile fields.
/// </summary>
public class ModulusUser : IdentityUser<Guid>, IHasExtraProperties
{
    /// <summary>
    /// Values that other modules or the application add to the account (the <c>ExtraProperties</c> convention of
    /// <see cref="IHasExtraProperties"/>), keyed by field name. Stored as a JSON text column; never null. Needs a column in apps that
    /// already have users (docs/upgrades/identity-platform-2026-10.identity.*.sql).
    /// </summary>
    public Dictionary<string, string?> ExtraProperties { get; set; } = [];

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
    /// The person's work e-mail address: a contact address, separate from the sign-in address (<see cref="Microsoft.AspNetCore.Identity.IdentityUser{TKey}.Email"/>).
    /// Null when not set. Needs a column in apps that already have users.
    /// </summary>
    public string? WorkEmail { get; set; }

    /// <summary>
    /// The work e-mail address waiting for its owner's confirmation. It is not the work e-mail until confirmed. Null when none is pending.
    /// Needs a column in apps that already have users.
    /// </summary>
    public string? PendingWorkEmail { get; set; }

    /// <summary>The stamp of the newest confirmation link; a link works only while it carries this stamp. Null when none is out.</summary>
    public string? WorkEmailVerificationStamp { get; set; }

    /// <summary>When the newest confirmation was requested (the cooldown runs from it). Null when never requested.</summary>
    public DateTimeOffset? WorkEmailRequestedAt { get; set; }

    /// <summary>Failed confirmation attempts since the newest request. At the limit, confirmation is refused until a new request. Needs a column.</summary>
    public int ConfirmationFailures { get; set; }

    /// <summary>The person's job title. Null when not set. Needs a column in apps that already have users.</summary>
    public string? JobTitle { get; set; }

    /// <summary>The department the person works in. Null when not set. Needs a column in apps that already have users.</summary>
    public string? Department { get; set; }

    /// <summary>
    /// The opaque id of the person's uploaded photo (premodule 3). The photo is stored under a key derived from this id and the tenant
    /// (<see cref="Modulus.Identity.Abstractions.ProfilePhotoRules.StoragePath"/>), so the account refers to a stored file, never to an
    /// arbitrary address. Null when there is no uploaded photo. Needs a column in apps that already have users.
    /// </summary>
    public Guid? PhotoAssetId { get; set; }

    /// <summary>
    /// The org unit (branch) the person works in primarily. Set by an administrator (<c>PATCH identity/users/{id}/organization</c>), not by the
    /// person. Null when not set. Needs a column in apps that already have users.
    /// </summary>
    public Guid? PrimaryBranchId { get; set; }

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
