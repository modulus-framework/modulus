namespace Modulus.UI.Theming;

/// <summary>
/// The standard layout names every theme must implement. Pages reference these
/// logical names; <see cref="ThemeOptions.Layouts"/> can remap them to shell
/// variants, and <see cref="ITheme.GetLayout"/> resolves the physical view.
/// </summary>
public static class StandardLayouts
{
    /// <summary>Authenticated app shell: sidebar + topbar + content.</summary>
    public const string Application = "Application";

    /// <summary>Centered card shell for login / MFA / password reset.</summary>
    public const string Account = "Account";

    /// <summary>Bare document: assets + RenderBody only.</summary>
    public const string Empty = "Empty";

    /// <summary>Public shell: topbar + content + footer, no sidebar.</summary>
    public const string Public = "Public";

    /// <summary>All standard layout names.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Application,
        Account,
        Empty,
        Public,
    ];
}
