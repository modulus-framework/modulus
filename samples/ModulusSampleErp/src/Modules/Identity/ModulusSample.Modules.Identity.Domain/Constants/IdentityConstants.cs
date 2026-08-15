using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Domain.Constants;

public static class Schemas
{
    public const string Identity = "identity";
}

public static class UserStatuses
{
    public const string Active = "active";
    public const string Inactive = "inactive";
    public const string Pending = "pending";
    public const string Suspended = "suspended";
    public const string Locked = "locked";
}

public static class PasswordPolicies
{
    public const int MinLength = 8;
    public const int MaxLength = 128;
    public const bool RequireUppercase = true;
    public const bool RequireLowercase = true;
    public const bool RequireDigit = true;
    public const bool RequireSpecialChar = true;
}

public static class SessionTimeouts
{
    public const int DefaultTimeoutMinutes = 30;
    public const int RememberMeTimeoutDays = 30;
    public const int AbsoluteTimeoutHours = 8;
}

public static class UserErrors
{
    public static readonly Error NotFound = Error.NotFound("User.NotFound", "User not found");
    public static readonly Error DuplicateEmail = Error.Conflict("User.DuplicateEmail", "A user with this email already exists");
    public static readonly Error DuplicateUsername = Error.Conflict("User.DuplicateUsername", "A user with this username already exists");
    public static readonly Error InvalidEmail = Error.Validation("User.InvalidEmail", "Invalid email format");
    public static readonly Error WeakPassword = Error.Validation("User.WeakPassword", "Password does not meet security requirements");
    public static readonly Error EmptyPassword = Error.Validation("User.EmptyPassword", "Password cannot be empty");
    public static readonly Error InvalidStatus = Error.Validation("User.InvalidStatus", "Invalid user status");
    public static readonly Error CannotDeleteSystemUser = Error.BusinessRule("User.CannotDeleteSystemUser", "Cannot delete system user");
    public static readonly Error CannotDeleteOwnAccount = Error.BusinessRule("User.CannotDeleteOwnAccount", "Cannot delete your own account");
    public static readonly Error InvalidCredentials = Error.Validation("User.InvalidCredentials", "Invalid email or password");
    public static readonly Error AccountLocked = Error.BusinessRule("User.AccountLocked", "Account is locked");
    public static readonly Error AccountInactive = Error.BusinessRule("User.AccountInactive", "Account is inactive");
    public static readonly Error AccountPending = Error.BusinessRule("User.AccountPending", "Account is pending activation");
}

public static class RoleErrors
{
    public static readonly Error NotFound = Error.NotFound("Role.NotFound", "Role not found");
    public static readonly Error DuplicateName = Error.Conflict("Role.DuplicateName", "A role with this name already exists");
    public static readonly Error EmptyName = Error.Validation("Role.EmptyName", "Role name cannot be empty");
    public static readonly Error CannotDeleteSystemRole = Error.BusinessRule("Role.CannotDeleteSystemRole", "Cannot delete system role");
    public static readonly Error CannotDeleteRoleWithUsers = Error.BusinessRule("Role.CannotDeleteRoleWithUsers", "Cannot delete role with assigned users");
}

public static class PermissionErrors
{
    public static readonly Error NotFound = Error.NotFound("Permission.NotFound", "Permission not found");
    public static readonly Error DuplicateCode = Error.Conflict("Permission.DuplicateCode", "A permission with this code already exists");
    public static readonly Error EmptyCode = Error.Validation("Permission.EmptyCode", "Permission code cannot be empty");
    public static readonly Error EmptyDescription = Error.Validation("Permission.EmptyDescription", "Permission description cannot be empty");
}