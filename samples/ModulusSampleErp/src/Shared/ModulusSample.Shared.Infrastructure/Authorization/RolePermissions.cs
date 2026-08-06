using ModulusSample.Shared.Domain;

namespace ModulusSample.Shared.Infrastructure.Authorization;

public static class RolePermissions
{
    private const string Admin = "Admin";
    private const string User = "User";

    public static IReadOnlyList<string> GetPermissionsForRole(string roleName) => roleName switch
    {
        Admin =>
        [
            AppPermissions.IdentityProfileViewOwn, AppPermissions.IdentityProfileManageOwn,
            AppPermissions.IdentityPasswordChangeOwn, AppPermissions.IdentityUserManageAll,
            AppPermissions.IdentityUserViewAll, AppPermissions.IdentityRoleManageAll,
            AppPermissions.IdentityAdmin
        ],
        User =>
        [
            AppPermissions.IdentityProfileViewOwn, AppPermissions.IdentityProfileManageOwn,
            AppPermissions.IdentityPasswordChangeOwn
        ],
        _ => []
    };
}
