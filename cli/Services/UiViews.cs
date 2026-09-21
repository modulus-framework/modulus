using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Modulus.Cli.Services;

/// <summary>What sort of framework view a <see cref="UiView"/> is, which decides where the app's copy lives.</summary>
internal enum UiViewKind
{
    /// <summary>A Modulus.UI.Core component view (<c>Card</c>, <c>DataTable</c>, ...): <c>Views/Shared/Modulus/{Component}/{View}.cshtml</c>.</summary>
    Component,

    /// <summary>A page, partial, view start or view imports of a feature UI package (Users, Identity, ...): <c>Pages/{View}.cshtml</c>.</summary>
    Page,

    /// <summary>A layout, shell partial or error page of a theme: <c>Themes/{Theme}/{View}.cshtml</c>.</summary>
    Theme,
}

/// <summary>
/// One framework view, as embedded in the CLI: <see cref="Source"/> always has LF line endings.
/// </summary>
/// <param name="Component">
/// What the user names on the command line: the component (<c>Card</c>), the feature UI package (<c>Users</c>, or <c>Shared</c> for UI.Core's
/// shared partials) or the theme (<c>Tabler</c>).
/// </param>
/// <param name="View">
/// The view within it: <c>Default</c> for a component; the path below <c>Pages/</c> for a feature UI (<c>Users/Details</c>,
/// <c>Users/_UserCard</c>); the path below <c>Themes/{Theme}/</c> for a theme (<c>Layouts/Application</c>).
/// </param>
/// <param name="Source">The view's source, LF line endings.</param>
/// <param name="Kind">Which sort of view it is.</param>
internal sealed record UiView(string Component, string View, string Source, UiViewKind Kind = UiViewKind.Component)
{
    /// <summary>Short content hash used to tell whether the framework's copy changed since an eject.</summary>
    public string Hash => UiViewMarker.Hash(Source);

    /// <summary>Where the app's copy lives, relative to the host project, with forward slashes. The app's file at this path wins over the package's.</summary>
    public string AppPath => Kind switch
    {
        UiViewKind.Component => $"Views/Shared/Modulus/{Component}/{View}.cshtml",
        UiViewKind.Page => $"Pages/{View}.cshtml",
        _ => $"Themes/{Component}/{View}.cshtml",
    };

    /// <summary>
    /// The name <c>ui eject</c> takes for this single view: <c>Card</c> (or <c>Card:Compact</c>) for a component, the page's own path for a feature UI
    /// (<c>Users/Details</c>: unique across packages, so the package is left out), <c>Tabler/Layouts/Application</c> for a theme.
    /// </summary>
    public string Target => Kind switch
    {
        UiViewKind.Component => View == "Default" ? Component : $"{Component}:{View}",
        UiViewKind.Page => View,
        _ => $"{Component}/{View}",
    };

    /// <summary>True for a <c>_ViewImports</c> file: compile-time only, so a copy of a view is useless without the imports next to it.</summary>
    public bool IsViewImports => View.Equals("_ViewImports", StringComparison.OrdinalIgnoreCase)
        || View.EndsWith("/_ViewImports", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The framework's views embedded in the CLI, so an app can take ownership of one (<c>modulus ui eject Card</c>,
/// <c>ui eject Users/Details</c>, <c>ui eject Tabler</c>) and later see how it drifted from the framework's current version
/// (<c>modulus ui diff</c>). The views are compiled into the UI packages, so the packages themselves have no source to copy.
/// </summary>
internal static class UiViewCatalog
{
    private const string ComponentPrefix = "UiViews/";
    private const string PagePrefix = "UiPages/";
    private const string ThemePrefix = "UiThemes/";

    private static readonly Lazy<IReadOnlyList<UiView>> s_all = new(Load);

    public static IReadOnlyList<UiView> All => s_all.Value;

    /// <summary>Distinct component names, sorted.</summary>
    public static IReadOnlyList<string> Components
        => All.Where(v => v.Kind == UiViewKind.Component).Select(v => v.Component).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Feature UI packages and themes (everything that is not a component), sorted.</summary>
    public static IReadOnlyList<string> Groups
        => All.Where(v => v.Kind != UiViewKind.Component).Select(v => v.Component).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The views of one component, feature UI package or theme (case-insensitive), in name order.</summary>
    public static IReadOnlyList<UiView> ViewsOf(string component)
        => All.Where(v => string.Equals(v.Component, component, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>The view for <paramref name="component"/> (case-insensitive), or null when the framework has none.</summary>
    public static UiView? TryFind(string component, string view = "Default")
        => All.FirstOrDefault(v =>
            string.Equals(v.Component, component, StringComparison.OrdinalIgnoreCase)
            && string.Equals(v.View, view, StringComparison.OrdinalIgnoreCase));

    /// <summary>Like <see cref="TryFind"/> but throws a message listing what exists.</summary>
    public static UiView Find(string component, string view = "Default")
        => TryFind(component, view)
            ?? throw new InvalidOperationException(
                Components.Any(c => string.Equals(c, component, StringComparison.OrdinalIgnoreCase))
                    ? $"Component '{component}' has no view named '{view}'."
                    : $"Unknown component '{component}'. Available: {string.Join(", ", Components)}.");

    /// <summary>
    /// What a name on the command line stands for: a component (all its views), a feature UI package or theme (all its views), or one
    /// view of a feature UI or theme by its path (<c>Users/Details</c>, or with the package in front: <c>Users/Users/Details</c>,
    /// <c>Tabler/Layouts/Application</c>). <c>Card:Compact</c> names one view of a component. Case-insensitive; throws a message
    /// listing what exists when nothing matches.
    /// </summary>
    public static IReadOnlyList<UiView> Match(string target)
    {
        var name = (target ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
        if (name.Length == 0)
        {
            throw new InvalidOperationException("Name what to eject.");
        }

        var colon = name.IndexOf(':', StringComparison.Ordinal);
        if (colon > 0)
        {
            return [Find(name[..colon], name[(colon + 1)..])];
        }

        if (ViewsOf(name) is { Count: > 0 } whole)
        {
            return whole;
        }

        // A feature UI view is unique by its path (Users/Details), so the package may be left out; a theme's path is not
        // (Layouts/Application in two themes), so it needs the theme's name.
        var single = All.Where(v => v.Kind != UiViewKind.Component && (
                string.Equals($"{v.Component}/{v.View}", name, StringComparison.OrdinalIgnoreCase)
                || (v.Kind == UiViewKind.Page && string.Equals(v.View, name, StringComparison.OrdinalIgnoreCase))))
            .ToList();
        if (single.Count > 0)
        {
            return single;
        }

        throw new InvalidOperationException(
            $"Unknown UI target '{target}'. Components: {string.Join(", ", Components)}. " +
            $"Feature UIs and themes: {string.Join(", ", Groups)} (or one view of them, e.g. Users/Details or Tabler/Layouts/Application).");
    }

    /// <summary>
    /// The <c>_ViewImports</c> view a copy of <paramref name="view"/> needs beside it to compile: the nearest one at or above its folder
    /// within the same package or theme. Null for a component (those share one folder-wide file, see <c>UiEject.ViewImports</c>), for a
    /// <c>_ViewImports</c> itself, or when there is none.
    /// </summary>
    public static UiView? ImportsFor(UiView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Kind == UiViewKind.Component || view.IsViewImports)
        {
            return null;
        }

        var folder = ParentOf(view.View);
        while (true)
        {
            if (TryFind(view.Component, folder.Length == 0 ? "_ViewImports" : $"{folder}/_ViewImports") is { } found)
            {
                return found;
            }

            if (folder.Length == 0)
            {
                return null;
            }

            folder = ParentOf(folder);
        }
    }

    private static string ParentOf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    private static IReadOnlyList<UiView> Load()
    {
        var assembly = typeof(UiViewCatalog).Assembly;
        var views = new List<UiView>();

        foreach (var name in assembly.GetManifestResourceNames())
        {
            var (prefix, kind) = name.StartsWith(ComponentPrefix, StringComparison.Ordinal) ? (ComponentPrefix, UiViewKind.Component)
                : name.StartsWith(PagePrefix, StringComparison.Ordinal) ? (PagePrefix, UiViewKind.Page)
                : name.StartsWith(ThemePrefix, StringComparison.Ordinal) ? (ThemePrefix, UiViewKind.Theme)
                : (string.Empty, UiViewKind.Component);
            if (prefix.Length == 0 || !name.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // "UiViews/Card\Default.cshtml": MSBuild's %(RecursiveDir) keeps the platform's separator.
            var parts = name[prefix.Length..].Replace('\\', '/').Split('/');
            if (parts.Length < 2 || (kind == UiViewKind.Component && parts.Length != 2))
            {
                continue;
            }

            var view = string.Join('/', parts.Skip(1));
            view = view[..^".cshtml".Length];

            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded view not found: {name}");
            using var reader = new StreamReader(stream);
            views.Add(new UiView(parts[0], view, UiViewMarker.Normalize(reader.ReadToEnd()), kind));
        }

        return views
            .OrderBy(v => v.Kind)
            .ThenBy(v => v.Component, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.View, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

/// <summary>
/// The one-line Razor comment <c>modulus ui eject</c> puts at the top of an ejected view, recording which framework
/// version and which content (by hash) it was copied from. <c>ui diff</c> compares that hash with the framework's
/// current one, which is what tells "you customized it" from "the framework changed since you ejected it".
/// </summary>
internal static partial class UiViewMarker
{
    /// <summary>What was recorded at eject time.</summary>
    public sealed record Info(string Component, string View, string BaseHash, string FrameworkVersion);

    [GeneratedRegex(@"\A@\*\s*modulus-eject\s+component=(?<c>\S+)\s+view=(?<v>\S+)\s+base=(?<h>[0-9a-f]+)\s+framework=(?<f>\S+)[^\n]*\*@\n")]
    private static partial Regex MarkerLine();

    /// <summary>CRLF (and lone CR) to LF, so a file edited on Windows compares equal to the framework's.</summary>
    public static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>First 16 hex characters of the SHA-256 of the LF-normalized text.</summary>
    public static string Hash(string text)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(text))))[..16];

    /// <summary>The marker line (with its trailing newline) for a copy of <paramref name="view"/>.</summary>
    public static string Build(UiView view, string frameworkVersion)
        => $"@* modulus-eject component={view.Component} view={view.View} base={view.Hash} framework={frameworkVersion} - run `modulus ui diff` to compare with the framework *@\n";

    /// <summary>
    /// Splits an ejected file into its marker and the view body. Returns false (body = the whole text) for a file
    /// that has no marker, such as an override written by hand.
    /// </summary>
    public static bool TryParse(string content, [NotNullWhen(true)] out Info? info, out string body)
    {
        var text = Normalize(content);
        var match = MarkerLine().Match(text);
        if (!match.Success)
        {
            info = null;
            body = text;
            return false;
        }

        info = new Info(match.Groups["c"].Value, match.Groups["v"].Value, match.Groups["h"].Value, match.Groups["f"].Value);
        body = text[match.Length..];
        return true;
    }
}
