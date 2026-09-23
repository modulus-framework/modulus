using System.Text;
using System.Xml.Linq;

namespace Modulus.Cli.Services;

/// <summary>What <see cref="UiEject.Eject"/> did with one view.</summary>
internal enum UiEjectOutcome
{
    /// <summary>The file was written (or would be, on a dry run).</summary>
    Written,

    /// <summary>A file was already there and <c>--force</c> was not given.</summary>
    SkippedExists,
}

/// <summary>
/// Copies a framework view into the app so the app owns it (<c>modulus ui eject</c>). The app's file at the view's own path
/// (<see cref="UiView.AppPath"/>) is served instead of the package's: a component override at <c>Views/Shared/Modulus/{Component}/{View}.cshtml</c>
/// is the first stop of the component resolution chain (app, then theme, then framework default); a page, partial or layout is found
/// by its absolute path (<c>/Pages/Users/Details.cshtml</c>, <c>/Themes/Tabler/Layouts/Application.cshtml</c>), where the entry
/// assembly's file beats a package's. Either way it takes effect with no registration.
/// </summary>
internal static class UiEject
{
    /// <summary>Imports the ejected component views need; the <c>global::</c> is required (see the comment in the file).</summary>
    public const string ViewImports =
        "@* Imports for the Modulus component views ejected into this folder (modulus ui eject).\n" +
        "   \"global::\" is required: this folder's generated namespace has a \"Modulus\" segment that would\n" +
        "   otherwise shadow the root Modulus namespace. *@\n" +
        "@using global::Modulus.UI\n" +
        "@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers\n" +
        "@addTagHelper *, Modulus.UI.Core\n";

    private const string CorePackage = "Cobytelabs.Modulus.UI.Core";

    /// <summary>The UI project's directory for the app containing <paramref name="startDir"/> (where <c>Views/</c>, <c>Pages/</c> and <c>Themes/</c> live).</summary>
    public static string ResolveApiDir(string startDir)
    {
        var inventory = ModuleDiscovery.Inventory(startDir)
            ?? throw new InvalidOperationException(
                "No .slnx found in the current directory tree. Run from inside a Modulus application, or pass --output <path>.");

        if (inventory.Kind == AppKind.Api)
            throw new InvalidOperationException(
                $"This app is API-only ({AppKinds.Property}=api), so it has no UI views to override.");

        return inventory.UiProjectPath is { Length: > 0 } project && File.Exists(project)
            ? Path.GetDirectoryName(project)!
            : throw new InvalidOperationException("The app has no UI project (*.Api.csproj or *.Web.csproj) to hold the view overrides.");
    }

    /// <summary>The folder that holds an app's component overrides.</summary>
    public static string OverrideRoot(string apiDir) => Path.Combine(apiDir, "Views", "Shared", "Modulus");

    /// <summary>Where an app's override of <paramref name="view"/> lives.</summary>
    public static string PathFor(string apiDir, UiView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return Path.Combine([apiDir, .. view.AppPath.Split('/')]);
    }

    /// <summary>The NuGet package a view ships in (what the app must reference for the view to have anything to override).</summary>
    public static string? PackageFor(UiView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Kind == UiViewKind.Component || string.Equals(view.Component, "Shared", StringComparison.OrdinalIgnoreCase))
        {
            return CorePackage;
        }

        return UiModuleCatalog.All.FirstOrDefault(m => string.Equals(m.Name, view.Component, StringComparison.OrdinalIgnoreCase))?.PackageId;
    }

    /// <summary>
    /// True when the host project references the package <paramref name="view"/> belongs to (as a package or, inside the framework's own
    /// repository, as a project). Reads the references directly: a Central Package Management app has no <c>Version</c> on them.
    /// </summary>
    public static bool IsInstalled(string apiDir, UiView view)
    {
        var package = PackageFor(view);
        if (package is null)
        {
            return false;
        }

        var project = Directory.EnumerateFiles(apiDir, "*.csproj").FirstOrDefault();
        if (project is null)
        {
            return false;
        }

        var projectName = package.StartsWith("Cobytelabs.", StringComparison.OrdinalIgnoreCase) ? package["Cobytelabs.".Length..] : package;
        return XDocument.Load(project).Descendants().Any(e =>
            (e.Name.LocalName == "PackageReference"
                && string.Equals((string?)e.Attribute("Include"), package, StringComparison.OrdinalIgnoreCase))
            || (e.Name.LocalName == "ProjectReference"
                && ((string?)e.Attribute("Include"))?.Replace('\\', '/').EndsWith($"/{projectName}.csproj", StringComparison.OrdinalIgnoreCase) == true));
    }

    /// <summary>
    /// The <c>_ViewImports</c> a copy of <paramref name="view"/> still needs beside it to compile, or null when it is there already (or the view
    /// needs none). Views compile in the app's own assembly, so without the package's imports the tag helpers and <c>@using</c>s are unknown.
    /// </summary>
    public static UiView? PendingImports(string apiDir, UiView view)
    {
        var imports = UiViewCatalog.ImportsFor(view);
        return imports is not null && !File.Exists(PathFor(apiDir, imports)) ? imports : null;
    }

    /// <summary>
    /// Writes the app's copy of <paramref name="view"/>: a marker line (framework version + hash of the original) then the
    /// framework's source, LF line endings, and the <c>_ViewImports.cshtml</c> it compiles with when the app has none there yet.
    /// An existing file is left alone unless <paramref name="force"/>. <paramref name="dryRun"/> reports without writing.
    /// </summary>
    public static UiEjectOutcome Eject(string apiDir, UiView view, string frameworkVersion, bool force, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(view);

        var path = PathFor(apiDir, view);
        if (File.Exists(path) && !force)
        {
            return UiEjectOutcome.SkippedExists;
        }

        if (!dryRun)
        {
            Write(path, view, frameworkVersion);
            if (view.Kind == UiViewKind.Component)
            {
                EnsureViewImports(apiDir);
            }
            else if (PendingImports(apiDir, view) is { } imports)
            {
                Write(PathFor(apiDir, imports), imports, frameworkVersion);
            }
        }

        return UiEjectOutcome.Written;
    }

    private static void Write(string path, UiView view, string frameworkVersion)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, UiViewMarker.Build(view, frameworkVersion) + view.Source, new UTF8Encoding(false));
    }

    /// <summary>Creates <c>Views/Shared/Modulus/_ViewImports.cshtml</c> when the app has none. Returns true when it wrote it.</summary>
    public static bool EnsureViewImports(string apiDir)
    {
        var path = Path.Combine(OverrideRoot(apiDir), "_ViewImports.cshtml");
        if (File.Exists(path))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ViewImports, new UTF8Encoding(false));
        return true;
    }

    /// <summary>
    /// True when an existing <c>_ViewImports.cshtml</c> does not import <c>Modulus.UI</c>, in which case ejected views
    /// will not compile. (A missing one is created by <see cref="EnsureViewImports"/>.)
    /// </summary>
    public static bool ViewImportsLackModulusUi(string apiDir)
    {
        var path = Path.Combine(OverrideRoot(apiDir), "_ViewImports.cshtml");
        return File.Exists(path) && !File.ReadAllText(path).Contains("Modulus.UI", StringComparison.Ordinal);
    }
}
