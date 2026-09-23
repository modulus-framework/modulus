using System.Xml.Linq;

namespace Modulus.Cli.Services;

/// <summary>What a generated host is: API only, Razor Pages web app only, or both separately deployable.</summary>
internal enum AppKind
{
    /// <summary>An API host. No UI is created: no Razor Pages, no theme, no UI packages.</summary>
    Api,

    /// <summary>A Razor Pages web app (no exposed API surface). Single project; pages call IMediator in-process.</summary>
    WebApp,

    /// <summary>
    /// A web app (<b>separate</b> Razor Pages project) <b>and</b> a separate API project. The web app calls the API
    /// over HTTP via typed clients; the API serves both the web app and external clients (mobile, desktop, other systems).
    /// </summary>
    WebAppApi,
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
    public static readonly string[] Names = ["api", "webapp", "webapp+api"];

    /// <summary>The value written to <c>--kind</c> and the csproj property.</summary>
    public static string Name(this AppKind kind) => kind switch
    {
        AppKind.Api => "api",
        AppKind.WebApp => "webapp",
        AppKind.WebAppApi => "webapp+api",
        _ => throw new ArgumentException($"Unknown app kind: {kind}"),
    };

    /// <summary>Short label for messages.</summary>
    public static string Label(this AppKind kind) => kind switch
    {
        AppKind.Api => "API",
        AppKind.WebApp => "Web app",
        AppKind.WebAppApi => "Web app + API",
        _ => throw new ArgumentException($"Unknown app kind: {kind}"),
    };

    /// <summary>
    /// Parses <c>api</c> / <c>webapp</c> / <c>webapp+api</c> (case-insensitive).
    /// Also accepts the legacy <c>web</c> alias, which maps to <c>webapp</c> for backward compatibility.
    /// </summary>
    public static AppKind Parse(string value)
    {
        var trimmed = value?.Trim() ?? "";
        return trimmed switch
        {
            var v when string.Equals(v, "api", StringComparison.OrdinalIgnoreCase) => AppKind.Api,
            var v when string.Equals(v, "webapp", StringComparison.OrdinalIgnoreCase) => AppKind.WebApp,
            var v when string.Equals(v, "webapp+api", StringComparison.OrdinalIgnoreCase) => AppKind.WebAppApi,
            var v when string.Equals(v, "web", StringComparison.OrdinalIgnoreCase) => AppKind.WebApp, // Legacy alias for backward compat
            _ => throw new ArgumentException($"Unknown app kind '{value}'. Valid: {string.Join(", ", Names)} (or legacy 'web' for webapp)."),
        };
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
    /// Whether <c>generate-crud</c> scaffolds the admin UI. A <c>webapp</c> or <c>webapp+api</c> host gets it by default
    /// (<c>--no-ui</c> opts out), an <c>api</c> host never does (asking for it is an error), and a host with no recorded kind
    /// keeps the original opt-in <c>--with-ui</c>.
    /// </summary>
    public static bool ResolveCrudUi(AppKind? kind, bool withUi, bool noUi)
    {
        if (withUi && noUi)
            throw new ArgumentException("--with-ui and --no-ui cannot be combined.");

        return kind switch
        {
            AppKind.Api when withUi => throw new InvalidOperationException(
                $"This app is API-only ({Property}=api), so it has no UI to scaffold. " +
                $"To make it a web app, set <{Property}>webapp</{Property}> in the host project and run `modulus ui add Tabler`."),
            AppKind.Api => false,
            AppKind.WebApp or AppKind.WebAppApi => !noUi,
            _ => withUi,
        };
    }
}
