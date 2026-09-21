namespace Modulus.UI.Files;

/// <summary>Permission names declared by the Files UI.</summary>
public static class FilesUiPermissions
{
    /// <summary>Upload, download, and delete files.</summary>
    public const string Manage = "files:manage";
}

/// <summary>Options for the Files UI, bound from the <c>FilesUi</c> section.</summary>
public sealed class FilesUiOptions
{
    public const string SectionName = "FilesUi";

    /// <summary>
    /// Razor Pages authorization policy applied to the <c>/Files</c> folder.
    /// Defaults to <c>null</c> (pages open) so the UI works without the
    /// authorization stack; set to <see cref="FilesUiPermissions.Manage"/>
    /// once <c>AddModulusAuthorization</c> wires the <c>:</c>-policy
    /// convention. Declared on the registry by <c>AddModulusFilesUi</c>.
    /// File serving must stay behind this gate: local storage URLs are
    /// unsigned, so the download handler is the access control.
    /// </summary>
    public string? RequirePermission { get; set; }

    /// <summary>Maximum accepted upload size in bytes. Defaults to 10 MiB.</summary>
    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;
}
