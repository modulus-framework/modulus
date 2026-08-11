namespace ModulusSample.Modules.Identity.Presentation;

public static class IdentityPermissions
{
    public const string IdentityProfileViewOwn = "identity:profile:view_own";
    public const string IdentityProfileManageOwn = "identity:profile:manage_own";
    public const string IdentityPasswordChangeOwn = "identity:password:change_own";
    public const string IdentityUserManageAll = "identity:user:manage_all";
    public const string IdentityUserViewAll = "identity:user:view_all";
    public const string IdentityRoleManageAll = "identity:role:manage_all";
    public const string IdentityAdmin = "identity:admin";

    public static IReadOnlySet<string> AllSet { get; } = new HashSet<string>
    {
        IdentityProfileViewOwn,
        IdentityProfileManageOwn,
        IdentityPasswordChangeOwn,
        IdentityUserManageAll,
        IdentityUserViewAll,
        IdentityRoleManageAll,
        IdentityAdmin
    };
}
