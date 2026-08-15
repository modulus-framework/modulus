namespace ModulusSample.Modules.Identity.Application.Permissions;

public static class IdentityPermissions
{
    public const string Module = "Identity";

    public static class Users
    {
        public const string Create = $"{Module}.Users.Create";
        public const string View = $"{Module}.Users.View";
        public const string Edit = $"{Module}.Users.Edit";
        public const string Delete = $"{Module}.Users.Delete";
        public const string ChangePassword = $"{Module}.Users.ChangePassword";
        public const string ResetPassword = $"{Module}.Users.ResetPassword";
        public const string Activate = $"{Module}.Users.Activate";
        public const string Deactivate = $"{Module}.Users.Deactivate";
        public const string Lock = $"{Module}.Users.Lock";
        public const string Unlock = $"{Module}.Users.Unlock";
    }

    public static class Roles
    {
        public const string Create = $"{Module}.Roles.Create";
        public const string View = $"{Module}.Roles.View";
        public const string Edit = $"{Module}.Roles.Edit";
        public const string Delete = $"{Module}.Roles.Delete";
        public const string AssignPermissions = $"{Module}.Roles.AssignPermissions";
    }

    public static class Permissions
    {
        public const string View = $"{Module}.Permissions.View";
        public const string Grant = $"{Module}.Permissions.Grant";
        public const string Revoke = $"{Module}.Permissions.Revoke";
    }

    public static class AllPermissions
    {
        public const string CreateUsers = Users.Create;
        public const string ViewUsers = Users.View;
        public const string EditUsers = Users.Edit;
        public const string DeleteUsers = Users.Delete;
        public const string ChangeUserPassword = Users.ChangePassword;
        public const string ResetUserPassword = Users.ResetPassword;
        public const string ActivateUsers = Users.Activate;
        public const string DeactivateUsers = Users.Deactivate;
        public const string LockUsers = Users.Lock;
        public const string UnlockUsers = Users.Unlock;
        public const string CreateRoles = Roles.Create;
        public const string ViewRoles = Roles.View;
        public const string EditRoles = Roles.Edit;
        public const string DeleteRoles = Roles.Delete;
        public const string AssignRolePermissions = Roles.AssignPermissions;
        public const string ViewPermissions = Permissions.View;
        public const string GrantPermissions = Permissions.Grant;
        public const string RevokePermissions = Permissions.Revoke;

        public static readonly string[] Values = new[]
        {
            CreateUsers, ViewUsers, EditUsers, DeleteUsers, ChangeUserPassword, ResetUserPassword, ActivateUsers, DeactivateUsers, LockUsers, UnlockUsers,
            CreateRoles, ViewRoles, EditRoles, DeleteRoles, AssignRolePermissions,
            ViewPermissions, GrantPermissions, RevokePermissions
        };
    }
}