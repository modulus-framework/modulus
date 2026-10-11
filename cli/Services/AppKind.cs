using System.Xml.Linq;

namespace Modulus.Cli.Services;

/// <summary>What a generated host is: API only, Razor Pages web app only, or both separately deployable.</summary>
internal enum AppKind
{
    /// <summary>An API host. No UI is created: no Razor Pages, no theme, no UI packages.</summary>
    Api,

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
    public static readonly string[] Names = ["api", "webapp+api"];

    /// <summary>The value written to <c>--kind</c> and the csproj property.</summary>
    public static string Name(this AppKind kind) => kind switch
    {
        AppKind.Api => "api",
        AppKind.WebAppApi => "webapp+api",
        _ => throw new ArgumentException($"Unknown app kind: {kind}"),
    };

    /// <summary>Short label for messages.</summary>
    public static string Label(this AppKind kind) => kind switch
    {
        AppKind.Api => "API",
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
            var v when string.Equals(v, "webapp", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "web", StringComparison.OrdinalIgnoreCase)
                => throw new ArgumentException("The single-project 'webapp' kind was retired with the older UI modules. Use --kind webapp+api (a Web project on the Modulus UI framework plus the API)."),
            var v when string.Equals(v, "webapp+api", StringComparison.OrdinalIgnoreCase) => AppKind.WebAppApi,
            _ => throw new ArgumentException($"Unknown app kind '{value}'. Valid: {string.Join(", ", Names)}."),
        };
    }

    /// <summary>The kind recorded in the host project, or null when there is none (a host from before app kinds).</summary>
    public static AppKind? Read(string? apiCsprojPath)
    {
        if (string.IsNullOrEmpty(apiCsprojPath) || !File.Exists(apiCsprojPath))
            return null;

        var value = XDocument.Load(apiCsprojPath).Descendants(Property).FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(value))
            return null;
        // An app created as the retired single-project 'webapp' is left unconstrained, like one from before app kinds.
        return value.Trim().ToLowerInvariant() is "webapp" or "web" ? null : Parse(value);
    }

}
