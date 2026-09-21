namespace Modulus.UI.Theming;

/// <summary>
/// Theme selection and per-page-layout mapping, bound from the
/// <c>Modulus:Ui:Theme</c> configuration section. Layout names are logical
/// (see <see cref="StandardLayouts"/>); <see cref="Layouts"/> remaps them to
/// shell variants so apps can say "render the Admin layout with the
/// Application shell" without new page code.
/// </summary>
public sealed class ThemeOptions
{
    /// <summary>Configuration section bound by <c>ConfigureModulusUi</c>.</summary>
    public const string SectionName = "Modulus:Ui:Theme";

    /// <summary>Name of the active <see cref="ITheme"/>. Default: <c>Tabler</c>.</summary>
    public string Name { get; set; } = "Tabler";

    /// <summary>
    /// Logical layout name → shell variant passed to
    /// <see cref="ITheme.GetLayout"/>. Seeded with identity mappings for every
    /// <see cref="StandardLayouts.All"/> entry by <c>ConfigureModulusUi</c>.
    /// </summary>
    public Dictionary<string, string> Layouts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initial color mode stamped on <c>&lt;html data-theme&gt;</c>.</summary>
    public string ColorMode { get; set; } = "light";

    /// <summary>
    /// Renders the theme switcher and honors the persisted user preference
    /// (via <c>IUserUiPreferenceStore</c>, when registered).
    /// </summary>
    public bool AllowUserThemeSwitch { get; set; }

    /// <summary>Resolves a logical layout name to the shell variant for <see cref="ITheme.GetLayout"/>.</summary>
    public string ResolveLayout(string layoutName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutName);
        return Layouts.TryGetValue(layoutName, out var variant) ? variant : layoutName;
    }
}
