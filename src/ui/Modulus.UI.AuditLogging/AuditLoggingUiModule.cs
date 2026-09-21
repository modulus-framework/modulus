namespace Modulus.UI.AuditLogging;

using Modulus.Core.Abstractions;

/// <summary>
/// Navigation sidecar for the Audit Logging UI: filterable browser plus
/// details over the audit-log store.
/// </summary>
public sealed class AuditLoggingUiModule : UiModule
{
    public override ModuleManifest Manifest { get; } = new(
        "Modulus.AuditLogging",
        "AuditLogging",
        "1.0.0",
        ["Modulus.Platform", "Modulus.UI.Core"],
        ["AuditLogs"]);

    public override void ConfigureNavigation(UiNavigationBuilder navigation)
    {
        navigation.AddItem(
            "AuditLogging.Browser",
            "Audit logs",
            "/audit-logs",
            groupId: "Administration",
            icon: "clipboard-list",
            requiredPermission: AuditLoggingUiPermissions.View,
            order: 40);
    }
}
