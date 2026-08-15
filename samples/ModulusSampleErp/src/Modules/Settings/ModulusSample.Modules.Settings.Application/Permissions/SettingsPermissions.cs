namespace ModulusSample.Modules.Settings.Application.Permissions;

public static class SettingsPermissions
{
    public const string Module = "Settings";

    public static class SystemSettings
    {
        public const string Create = $"{Module}.SystemSettings.Create";
        public const string View = $"{Module}.SystemSettings.View";
        public const string Edit = $"{Module}.SystemSettings.Edit";
        public const string Delete = $"{Module}.SystemSettings.Delete";
    }

    public static class TenantSettings
    {
        public const string Create = $"{Module}.TenantSettings.Create";
        public const string View = $"{Module}.TenantSettings.View";
        public const string Edit = $"{Module}.TenantSettings.Edit";
        public const string Delete = $"{Module}.TenantSettings.Delete";
    }

    public static class AllPermissions
    {
        public const string CreateSystemSettings = SystemSettings.Create;
        public const string ViewSystemSettings = SystemSettings.View;
        public const string EditSystemSettings = SystemSettings.Edit;
        public const string DeleteSystemSettings = SystemSettings.Delete;
        public const string CreateTenantSettings = TenantSettings.Create;
        public const string ViewTenantSettings = TenantSettings.View;
        public const string EditTenantSettings = TenantSettings.Edit;
        public const string DeleteTenantSettings = TenantSettings.Delete;

        public static readonly string[] Values = new[]
        {
            CreateSystemSettings, ViewSystemSettings, EditSystemSettings, DeleteSystemSettings,
            CreateTenantSettings, ViewTenantSettings, EditTenantSettings, DeleteTenantSettings
        };
    }
}