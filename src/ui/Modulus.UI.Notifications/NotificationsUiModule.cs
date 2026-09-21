namespace Modulus.UI.Notifications;

using Modulus.Core.Abstractions;

/// <summary>
/// Navigation sidecar for the Notifications UI: per-user inbox over the
/// notification store (mark-read / mark-all-read / delete).
/// </summary>
public sealed class NotificationsUiModule : UiModule
{
    public override ModuleManifest Manifest { get; } = new(
        "Modulus.Notifications",
        "Notifications",
        "1.0.0",
        ["Modulus.Platform", "Modulus.UI.Core"],
        ["Notifications"]);

    public override void ConfigureNavigation(UiNavigationBuilder navigation)
    {
        navigation.AddItem(
            "Notifications.Inbox",
            "Notifications",
            "/notifications",
            groupId: "Administration",
            icon: "bell",
            requiredPermission: NotificationsUiPermissions.View,
            order: 50);
    }
}
