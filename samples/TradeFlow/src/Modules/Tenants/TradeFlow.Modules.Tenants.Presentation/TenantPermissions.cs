namespace TradeFlow.Modules.Tenants.Presentation;

public static class TenantPermissions
{
    public const string TenantViewAll = "tenant:view_all";
    public const string TenantManageAll = "tenant:manage_all";
    public const string TenantCreate = "tenant:create";
    public const string TenantUpdate = "tenant:update";
    public const string TenantDelete = "tenant:delete";
    public const string TenantAdmin = "tenant:admin";

    public static IReadOnlySet<string> AllSet { get; } = new HashSet<string>
    {
        TenantViewAll,
        TenantManageAll,
        TenantCreate,
        TenantUpdate,
        TenantDelete,
        TenantAdmin
    };
}
