namespace ModulusSample.Modules.Features.Application.Permissions;

public static class FeaturesPermissions
{
    public const string Module = "Features";

    public static class Features
    {
        public const string Create = $"{Module}.Features.Create";
        public const string View = $"{Module}.Features.View";
        public const string Edit = $"{Module}.Features.Edit";
        public const string Delete = $"{Module}.Features.Delete";
        public const string Activate = $"{Module}.Features.Activate";
        public const string Deactivate = $"{Module}.Features.Deactivate";
    }

    public static class TenantFeatures
    {
        public const string Assign = $"{Module}.TenantFeatures.Assign";
        public const string Unassign = $"{Module}.TenantFeatures.Unassign";
        public const string Enable = $"{Module}.TenantFeatures.Enable";
        public const string Disable = $"{Module}.TenantFeatures.Disable";
        public const string View = $"{Module}.TenantFeatures.View";
        public const string EditConfiguration = $"{Module}.TenantFeatures.EditConfiguration";
    }

    public static class AllPermissions
    {
        public const string CreateFeatures = Features.Create;
        public const string ViewFeatures = Features.View;
        public const string EditFeatures = Features.Edit;
        public const string DeleteFeatures = Features.Delete;
        public const string ActivateFeatures = Features.Activate;
        public const string DeactivateFeatures = Features.Deactivate;
        public const string AssignTenantFeatures = TenantFeatures.Assign;
        public const string UnassignTenantFeatures = TenantFeatures.Unassign;
        public const string EnableTenantFeatures = TenantFeatures.Enable;
        public const string DisableTenantFeatures = TenantFeatures.Disable;
        public const string ViewTenantFeatures = TenantFeatures.View;
        public const string EditTenantFeatureConfiguration = TenantFeatures.EditConfiguration;

        public static readonly string[] Values = new[]
        {
            CreateFeatures, ViewFeatures, EditFeatures, DeleteFeatures, ActivateFeatures, DeactivateFeatures,
            AssignTenantFeatures, UnassignTenantFeatures, EnableTenantFeatures, DisableTenantFeatures, ViewTenantFeatures, EditTenantFeatureConfiguration
        };
    }
}