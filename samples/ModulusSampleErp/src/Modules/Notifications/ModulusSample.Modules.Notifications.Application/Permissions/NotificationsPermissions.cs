namespace ModulusSample.Modules.Notifications.Application.Permissions;

public static class NotificationsPermissions
{
    public const string Module = "Notifications";

    public static class Notifications
    {
        public const string Send = $"{Module}.Notifications.Send";
        public const string View = $"{Module}.Notifications.View";
        public const string ViewOwn = $"{Module}.Notifications.ViewOwn";
        public const string Cancel = $"{Module}.Notifications.Cancel";
        public const string Delete = $"{Module}.Notifications.Delete";
    }

    public static class Templates
    {
        public const string Create = $"{Module}.Templates.Create";
        public const string View = $"{Module}.Templates.View";
        public const string Edit = $"{Module}.Templates.Edit";
        public const string Delete = $"{Module}.Templates.Delete";
        public const string Activate = $"{Module}.Templates.Activate";
        public const string Deactivate = $"{Module}.Templates.Deactivate";
    }

    public static class AllPermissions
    {
        public const string SendNotifications = Notifications.Send;
        public const string ViewNotifications = Notifications.View;
        public const string ViewOwnNotifications = Notifications.ViewOwn;
        public const string CancelNotifications = Notifications.Cancel;
        public const string DeleteNotifications = Notifications.Delete;
        public const string CreateTemplates = Templates.Create;
        public const string ViewTemplates = Templates.View;
        public const string EditTemplates = Templates.Edit;
        public const string DeleteTemplates = Templates.Delete;
        public const string ActivateTemplates = Templates.Activate;
        public const string DeactivateTemplates = Templates.Deactivate;

        public static readonly string[] Values = new[]
        {
            SendNotifications, ViewNotifications, ViewOwnNotifications, CancelNotifications, DeleteNotifications,
            CreateTemplates, ViewTemplates, EditTemplates, DeleteTemplates, ActivateTemplates, DeactivateTemplates
        };
    }
}