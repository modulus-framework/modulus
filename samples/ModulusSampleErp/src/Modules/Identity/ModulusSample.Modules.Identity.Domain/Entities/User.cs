using ModulusSample.Modules.Identity.Domain.Enums;
using ModulusSample.Modules.Identity.Domain.Errors;
using ModulusSample.Modules.Identity.Domain.Events;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Domain.ValueObjects;
using PhoneNumber = ModulusSample.Shared.Domain.ValueObjects.PhoneNumber;

namespace ModulusSample.Modules.Identity.Domain.Entities;

public sealed class User : AggregateRoot
{
    // Constants

    private readonly List<UserRole> _userRoles = [];

    private User() { }

    private User(
        UserId id,
        Email email,
        UserName userName,
        string firstName,
        string lastName,
        UserType userType)
    {
        Id = id;
        Email = email;
        UserName = userName;
        FirstName = firstName;
        LastName = lastName;
        UserType = userType;
        Status = UserStatus.PendingEmailVerification;
        EmailConfirmed = false;
        PhoneNumberConfirmed = false;
        CreatedAtUtc = DateTime.UtcNow;
    }

    // ===== BASIC IDENTITY PROPERTIES =====
    public UserId Id { get; private set; }
    public Email Email { get; private set; }
    public UserName UserName { get; private set; }
    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public PhoneNumber? PhoneNumber { get; private set; }
    public string? ProfileImageUrl { get; private set; }
    public UserType UserType { get; private set; }

    // ===== SECURITY =====
    public bool EmailConfirmed { get; private set; }
    public bool PhoneNumberConfirmed { get; private set; }
    public string? PasswordHash { get; set; }

    // ===== AUDIT =====
    public UserStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? LastLoginAtUtc { get; private set; }
    public DateTime? LastActivityAtUtc { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }

    // FIX: these were public set, breaking aggregate encapsulation.
    // They are now private set and mutated only through domain methods.
    public string? CreatedBy { get; private set; }
    public string? LastModifiedBy { get; private set; }
    public DateTime? LastModifiedAtUtc { get; private set; }

    // ===== COMPUTED PROPERTIES =====
    public string FullName => $"{FirstName} {LastName}";

    public bool IsSystemAdministrator => UserType == UserType.Admin;

    // ===== COLLECTIONS =====
    public IReadOnlyCollection<UserRole> UserRoles => _userRoles.AsReadOnly();

    // ===== AUDIT SETTERS (for infrastructure use only) =====
    // Called by audit interceptors or command handlers — never by domain logic.
    public void SetCreatedBy(string createdBy) => CreatedBy = createdBy;
    public void SetLastModifiedBy(string modifiedBy) => LastModifiedBy = modifiedBy;

    // ===== FACTORY METHOD =====
    public static User Create(
        UserId userId,
        Email email,
        UserName userName,
        string firstName,
        string lastName,
        UserType userType,
        bool emailConfirmed = false)
    {
        var user = new User(userId, email, userName, firstName, lastName, userType);

        if (emailConfirmed)
        {
            user.EmailConfirmed = true;
            user.Status = UserStatus.Active;
        }

        user.Raise(new UserCreatedDomainEvent(
            user.Id, email.Value, userName.Value, firstName, lastName,
            userType, DateTime.UtcNow));

        return user;
    }

    // ===== PROFILE METHODS =====
    public void UpdateProfile(string firstName, string lastName, PhoneNumber? phoneNumber, string? profileImageUrl)
    {
        FirstName = firstName;
        LastName = lastName;
        PhoneNumber = phoneNumber;
        ProfileImageUrl = profileImageUrl;
        LastModifiedAtUtc = DateTime.UtcNow;
        IncrementVersion();

        Raise(new UserProfileUpdatedDomainEvent(Id));
    }

    /// <summary>
    /// Updates the user type. Administrative operation — use carefully.
    /// </summary>
    public void UpdateUserType(UserType newUserType)
    {
        UserType = newUserType;
        LastModifiedAtUtc = DateTime.UtcNow;
        IncrementVersion();

        Raise(new UserProfileUpdatedDomainEvent(Id));
    }

    // ===== EMAIL VERIFICATION =====
    public void VerifyEmail()
    {
        EmailConfirmed = true;
        Status = UserStatus.Active;
        LastModifiedAtUtc = DateTime.UtcNow;
        IncrementVersion();

        // Raise the same event as ConfirmEmail so downstream systems stay consistent.
        Raise(new UserEmailVerifiedEvent(Id, Email.Value));
    }

    // ===== ACTIVITY TRACKING =====
    public void UpdateLastLogin()
    {
        LastLoginAtUtc = DateTime.UtcNow;
        LastActivityAtUtc = DateTime.UtcNow;
        LastModifiedAtUtc = DateTime.UtcNow;
        IncrementVersion();
    }

    /// <summary>
    /// High-frequency ping — intentionally does NOT increment Version or
    /// LastModifiedAtUtc to avoid flooding the concurrency token.
    /// </summary>
    public void UpdateLastActivity()
    {
        LastActivityAtUtc = DateTime.UtcNow;
        // No IncrementVersion() — see summary above.
    }

    // ===== ROLE MANAGEMENT =====
    public void AddRole(RoleId roleId)
    {
        if (_userRoles.Any(ur => ur.RoleId == roleId))
        {
            return;
        }

        var userRole = UserRole.Create(Id, roleId);
        _userRoles.Add(userRole);
        LastModifiedAtUtc = DateTime.UtcNow;
        IncrementVersion();

        Raise(new RoleAssignedToUserDomainEvent(Id, roleId, DateTime.UtcNow));
    }

    public void RemoveRole(RoleId roleId)
    {
        UserRole? userRole = _userRoles.Find(ur => ur.RoleId == roleId);
        if (userRole is null)
        {
            return;
        }

        _userRoles.Remove(userRole);
        LastModifiedAtUtc = DateTime.UtcNow;
        IncrementVersion();

        Raise(new RoleRemovedFromUserDomainEvent(Id, roleId, DateTime.UtcNow));
    }

    // ===== PROFILE IMAGE =====
    public void UpdateProfileImage(string? profileImageUrl)
    {
        if (ProfileImageUrl == profileImageUrl)
        {
            return; // No change, do nothing
        }

        string? oldProfileImageUrl = ProfileImageUrl;
        ProfileImageUrl = profileImageUrl;
        LastModifiedAtUtc = DateTime.UtcNow;
        IncrementVersion();

        Raise(new UserProfilePhotoChangedEvent(Id, oldProfileImageUrl));
    }

    // ===== SOFT DELETE =====
    public void Delete(string reason)
    {
        IsDeleted = true;
        DeletedAtUtc = DateTime.UtcNow;
        Status = UserStatus.Deleted;

        // Anonymise PII in-place
        Email = Email.Create($"deleted_{Id.Value}@deleted.local").Value;
        FirstName = "DELETED";
        LastName = "USER";
        PhoneNumber = null;
        ProfileImageUrl = null;

        // Roles and consents are retained for audit purposes.
        // They are cleaned up by the data-retention policy via UserDeletedDomainEvent.

        LastModifiedAtUtc = DateTime.UtcNow;
        IncrementVersion();

        Raise(new UserDeletedDomainEvent(Id, reason, DateTime.UtcNow, ProfileImageUrl));
    }

    public void Suspend(string reason)
    {
        Status = UserStatus.Suspended;
        LastModifiedAtUtc = DateTime.UtcNow;
        IncrementVersion();

        Raise(new UserSuspendedDomainEvent(Id, reason, DateTime.UtcNow));
    }

    public void Activate()
    {
        Status = UserStatus.Active;
        LastModifiedAtUtc = DateTime.UtcNow;
        IncrementVersion();

        Raise(new UserActivatedDomainEvent(Id));
    }
}
