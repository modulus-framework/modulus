namespace Modulus.UI;

/// <summary>
/// Shell branding for the shared Tabler layout (<c>_UiLayout</c>), bound from
/// the <c>ModulusUi</c> section. Lets every host app rebrand the UI without
/// forking the RCL: set <c>AppName</c> (navbar brand + <c>&lt;title&gt;</c>
/// suffix), <c>BrandHref</c>, and an optional <c>FooterText</c>.
/// </summary>
public sealed class ModulusUiOptions
{
    public const string SectionName = "ModulusUi";

    /// <summary>Navbar brand + title suffix. Defaults to <c>Modulus</c>.</summary>
    public string AppName { get; set; } = "Modulus";

    /// <summary>Target of the navbar brand link. Defaults to <c>~/</c>.</summary>
    public string BrandHref { get; set; } = "~/";

    /// <summary>
    /// Optional footer line rendered under the page body. Defaults to
    /// <c>null</c> (no footer).
    /// </summary>
    public string? FooterText { get; set; }
}
