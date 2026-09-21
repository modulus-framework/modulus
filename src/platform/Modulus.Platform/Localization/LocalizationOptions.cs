namespace Modulus.Localization;

/// <summary>
/// Culture configuration for <c>AddModulusLocalization</c> /
/// <c>UseModulusRequestLocalization</c>. Binds the <c>Localization</c> section.
/// </summary>
public sealed class LocalizationOptions
{
    public const string SectionName = "Localization";

    /// <summary>Fallback culture when no translation matches. Default: en.</summary>
    public string DefaultCulture { get; set; } = "en";

    /// <summary>Cultures advertised to <c>RequestLocalizationMiddleware</c>.</summary>
    public string[] SupportedCultures { get; set; } = ["en"];
}
