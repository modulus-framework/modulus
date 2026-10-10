using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Modulus.Cli.Services;

/// <summary>
/// Languages for the Modulus UI framework: the culture names in <c>Modulus:Theme:Cultures</c> (the first is the default; two or more
/// show a language picker in the theme settings) and <c>app.UseModulusLocalization()</c> in the host's pipeline.
/// </summary>
internal static class UiI18n
{
    private const string UsingLine = "using Modulus.AspNetCore.Mvc;";
    private const string CallLine = "app.UseModulusLocalization();";

    private static readonly Lazy<Dictionary<string, string>> Known = new(() =>
        CultureInfo.GetCultures(CultureTypes.NeutralCultures | CultureTypes.SpecificCultures)
            .Where(c => c.Name.Length > 0)
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase));

    /// <summary>Splits <c>en,es-ES,fr</c>, checks every name against the runtime's cultures and drops duplicates.</summary>
    public static IReadOnlyList<string> ParseLanguages(string? languages)
    {
        var names = (languages ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length == 0)
            throw new ArgumentException("Pass the languages, e.g. --languages en,es,fr (culture names; the first is the default).");

        var result = new List<string>();
        foreach (var name in names)
        {
            // .NET accepts any well-formed name as a custom culture, so check against the cultures the runtime ships.
            var known = Known.Value.TryGetValue(name, out var canonical)
                ? canonical
                : throw new ArgumentException($"'{name}' is not a culture name this machine knows (examples: en, en-US, es, fr-FR, de, ja, zh-CN).");
            if (!result.Contains(known, StringComparer.OrdinalIgnoreCase))
                result.Add(known);
        }
        return result;
    }

    /// <summary>Writes <c>Modulus:Theme:Cultures</c>, keeping every other setting.</summary>
    public static string ApplyToAppSettings(string appSettingsJson, IReadOnlyList<string> cultures)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(appSettingsJson,
                documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? new JsonObject();
        }
        catch (JsonException ex) { throw new InvalidOperationException($"appsettings.json is not valid JSON: {ex.Message}"); }

        var modulus = root["Modulus"] as JsonObject ?? new JsonObject();
        var theme = modulus["Theme"] as JsonObject ?? new JsonObject();
        theme["Cultures"] = new JsonArray([.. cultures.Select(c => (JsonNode?)JsonValue.Create(c))]);
        modulus["Theme"] = theme;
        root["Modulus"] = modulus;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>
    /// Adds <c>app.UseModulusLocalization();</c> before <c>app.UseRouting();</c> (and the namespace using). Returns the text unchanged when
    /// it is already there, and null when there is no <c>UseRouting</c> to anchor on.
    /// </summary>
    public static string? EnsureLocalization(string program)
    {
        if (program.Contains("UseModulusLocalization", StringComparison.Ordinal))
            return program;

        var anchor = program.IndexOf("app.UseRouting();", StringComparison.Ordinal);
        if (anchor < 0)
            return null;

        var newline = program.Contains("\r\n") ? "\r\n" : "\n";
        var lineStart = program.LastIndexOf('\n', anchor) + 1;
        var updated = program.Insert(lineStart, CallLine + newline);

        if (!updated.Contains(UsingLine, StringComparison.Ordinal))
            updated = UsingLine + newline + updated;
        return updated;
    }
}
