using System.Xml.Linq;

namespace Modulus.Cli.Services;

/// <summary>What a generated host is: an API only, or a web app that also exposes the API.</summary>
internal enum AppKind
{
    /// <summary>An API host. No UI is created: no Razor Pages, no theme, no UI packages.</summary>
    Api,

    /// <summary>
    /// A web app (Razor Pages + HTMX UI) <b>and</b> the API, so external clients (mobile, desktop, other systems)
    /// can use the same modules the UI does.
    /// </summary>
    Web,
}

/// <summary>
/// Names the app kind and persists it in the host project (<c>&lt;ModulusAppKind&gt;</c>), so <c>generate-crud</c>,
/// <c>ui add</c> and <c>info</c> agree with what <c>modulus app</c> chose. A host without the property was generated
/// before app kinds existed and is left unconstrained (<c>null</c>), so older apps behave as they always did.
/// </summary>
internal static class AppKinds
{
    /// <summary>The MSBuild property in the host <c>.csproj</c> that records the kind.</summary>
    public const string Property = "ModulusAppKind";

    /// <summary>Valid values for <c>--kind</c>, in menu order.</summary>
    public static readonly string[] Names = ["api", "web"];

    /// <summary>The value written to <c>--kind</c> and the csproj property.</summary>
    public static string Name(this AppKind kind) => kind == AppKind.Web ? "web" : "api";

    /// <summary>Short label for messages: <c>API</c> or <c>Web app + API</c>.</summary>
    public static string Label(this AppKind kind) => kind == AppKind.Web ? "Web app + API" : "API";

    /// <summary>Parses <c>api</c> / <c>web</c> (case-insensitive); anything else is an <see cref="ArgumentException"/>.</summary>
    public static AppKind Parse(string value)
    {
        if (string.Equals(value?.Trim(), "api", StringComparison.OrdinalIgnoreCase)) return AppKind.Api;
        if (string.Equals(value?.Trim(), "web", StringComparison.OrdinalIgnoreCase)) return AppKind.Web;
        throw new ArgumentException($"Unknown app kind '{value}'. Valid: {string.Join(", ", Names)}.");
    }

    /// <summary>The kind recorded in the host project, or null when there is none (a host from before app kinds).</summary>
    public static AppKind? Read(string? apiCsprojPath)
    {
        if (string.IsNullOrEmpty(apiCsprojPath) || !File.Exists(apiCsprojPath))
            return null;

        var value = XDocument.Load(apiCsprojPath).Descendants(Property).FirstOrDefault()?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : Parse(value);
    }

    /// <summary>
    /// Whether <c>generate-crud</c> scaffolds the admin UI. A <c>web</c> host gets it by default (<c>--no-ui</c> opts
    /// out), an <c>api</c> host never does (asking for it is an error), and a host with no recorded kind keeps the
    /// original opt-in <c>--with-ui</c>.
    /// </summary>
    public static bool ResolveCrudUi(AppKind? kind, bool withUi, bool noUi)
    {
        if (withUi && noUi)
            throw new ArgumentException("--with-ui and --no-ui cannot be combined.");

        return kind switch
        {
            AppKind.Api when withUi => throw new InvalidOperationException(
                $"This app is API-only ({Property}=api), so it has no UI to scaffold. " +
                $"To make it a web app, set <{Property}>web</{Property}> in the host project and run `modulus ui add Tabler`."),
            AppKind.Api => false,
            AppKind.Web => !noUi,
            _ => withUi,
        };
    }
}
