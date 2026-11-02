using Microsoft.AspNetCore.Identity;
using Modulus.Core.Abstractions.Entities;

namespace Modulus.Identity.Abstractions;

/// <summary>
/// Base user entity for all Modulus applications: the ASP.NET Identity account, the company (tenant) it belongs to, and the account
/// state the framework itself needs (lifecycle, suspension, provisioning). Profile data (names, photos, job details, contact
/// addresses, shop-floor codes) is not part of the framework. An application subclasses this type and adds the columns it needs:
/// <code>
/// public sealed class AppUser : ModulusUser
/// {
///     public required string FirstName { get; set; }
///     public required string LastName { get; set; }
/// }
/// </code>
/// and passes it to <c>AddModulusIdentity&lt;AppUser, ...&gt;</c>. Hooks that map outside data onto the subclass are
/// <see cref="IExternalAccountProfile{TUser}"/> and the display-name directory (<c>IUserDisplayNameDirectory</c>).
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
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The identifier the provisioning system (SCIM client, HR system) uses for this person. Null for an account it did not
    /// create. Needs a column in apps that already have users.
    /// </summary>
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
}
