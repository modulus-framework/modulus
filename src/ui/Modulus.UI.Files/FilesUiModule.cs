namespace Modulus.UI.Files;

using Modulus.Core.Abstractions;

/// <summary>
/// Navigation sidecar for the Files UI: path-addressed manager over the
/// registered <c>IFileStorage</c> (upload / download / delete by path).
/// There is no directory listing — the storage contract has none — so the
/// page addresses files by explicit path.
/// </summary>
public sealed class FilesUiModule : UiModule
{
    public override ModuleManifest Manifest { get; } = new(
        "Modulus.Files",
        "Files",
        "1.0.0",
        ["Modulus.Platform", "Modulus.UI.Core"],
        ["Files"]);

    public override void ConfigureNavigation(UiNavigationBuilder navigation)
    {
        navigation.AddItem(
            "Files.Manager",
            "Files",
            "/files",
            groupId: "Administration",
            icon: "folder",
            requiredPermission: FilesUiPermissions.Manage,
            order: 60);
    }
}
