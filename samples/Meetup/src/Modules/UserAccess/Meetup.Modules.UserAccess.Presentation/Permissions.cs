namespace Meetup.Modules.UserAccess.Presentation;

/// <summary>Permission names for the UserAccess module (colon-style so the
/// framework's permission-policy provider enforces them server-side).</summary>
public static class UserAccessPermissions
{
    public const string ViewUsers = "useraccess:view";
    public const string DeactivateUser = "useraccess:deactivate";
}
