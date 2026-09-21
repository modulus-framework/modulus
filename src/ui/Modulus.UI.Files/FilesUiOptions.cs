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
    /// Defaults to <see cref="FilesUiPermissions.Manage"/> — every page model
    /// also carries a bare <c>[Authorize]</c> as an unconditional floor, so
    /// anonymous access is impossible even before
    /// <c>AddModulusAuthorization</c> wires the <c>:</c>-policy convention
    /// (without it, requests hit an unresolvable-policy error instead of
    /// silently serving admin pages — call <c>AddModulusAuthorization</c> to
    /// fix it). Set to <c>null</c> or <c>""</c> to opt out of the permission
    /// check and keep only the authentication floor. File serving must stay
    /// behind this gate: local storage URLs are unsigned, so the download
    /// handler is the access control.
    /// </summary>
    public string? RequirePermission { get; set; } = FilesUiPermissions.Manage;

    /// <summary>Maximum accepted upload size in bytes. Defaults to 10 MiB.</summary>
    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;
}
