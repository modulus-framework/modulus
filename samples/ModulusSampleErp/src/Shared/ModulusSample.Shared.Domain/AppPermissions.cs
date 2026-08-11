namespace ModulusSample.Shared.Domain;

public static class AppPermissions
{
    // Identity permissions - duplicates for backward compatibility, real source in ModulusSample.Modules.Identity.Presentation.IdentityPermissions
    public const string IdentityProfileViewOwn = "identity:profile:view_own";
    public const string IdentityProfileManageOwn = "identity:profile:manage_own";
    public const string IdentityPasswordChangeOwn = "identity:password:change_own";
    public const string IdentityUserManageAll = "identity:user:manage_all";
    public const string IdentityUserViewAll = "identity:user:view_all";
    public const string IdentityRoleManageAll = "identity:role:manage_all";
    public const string IdentityAdmin = "identity:admin";

    // Tenant permissions - duplicates for backward compatibility, real source in ModulusSample.Modules.Tenants.Presentation.TenantPermissions
    public const string TenantViewAll = "tenant:view_all";
    public const string TenantManageAll = "tenant:manage_all";
    public const string TenantCreate = "tenant:create";
    public const string TenantUpdate = "tenant:update";
    public const string TenantDelete = "tenant:delete";
    public const string TenantAdmin = "tenant:admin";

    public static IReadOnlySet<string> AllSet { get; } = new HashSet<string>
    {
        IdentityProfileViewOwn,
        IdentityProfileManageOwn,
        IdentityPasswordChangeOwn,
        IdentityUserManageAll,
        IdentityUserViewAll,
        IdentityRoleManageAll,
        IdentityAdmin,
        TenantViewAll,
        TenantManageAll,
        TenantCreate,
        TenantUpdate,
        TenantDelete,
        TenantAdmin
    };
}
