using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Modulus.Cli.Services;

/// <summary>
/// A named set of <c>Modulus:Theme</c> values (the keys of <c>ModulusThemeOptions</c>), kept in the app under
/// <c>Themes/{name}/theme.json</c> and applied to <c>appsettings.json</c> by <c>modulus ui theme set</c>.
/// </summary>
internal sealed record UiTheme(string Name, string Base, SortedDictionary<string, object> Values)
{
    public string PrimaryColor => (string)Values["PrimaryColor"];
}

internal static partial class UiThemes
{
    public const string FolderName = "Themes";
    public const string ManifestFile = "theme.json";
    public const string ActiveKey = "ActiveTheme";

    public static readonly string[] BaseNames = ["tabler", "minimal"];
    public static readonly string[] ExportFormats = ["css", "scss", "tailwind", "json"];

    /// <summary>The brand colours of <c>ModulusThemeOptions.ColorPresets</c>, so <c>--colors blue</c> means what the settings panel's blue means.</summary>
    public static readonly IReadOnlyDictionary<string, string> NamedColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["blue"] = "#066fd1",
        ["indigo"] = "#4263eb",
        ["purple"] = "#ae3ec9",
        ["pink"] = "#d6336c",
        ["red"] = "#d63939",
        ["orange"] = "#f76707",
        ["green"] = "#2fb344",
        ["teal"] = "#0ca678",
    };

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{0,39}$")]
    private static partial Regex NamePattern();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexPattern();

    public static string ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !NamePattern().IsMatch(name))
            throw new ArgumentException(
                $"'{name}' is not a valid theme name. Start with a letter; use letters, digits, '-' or '_' (40 characters at most).");
        return name;
    }

    /// <summary>Accepts <c>#rrggbb</c>, <c>rrggbb</c> or a preset name (<see cref="NamedColors"/>); returns <c>#rrggbb</c> in lower case.</summary>
    public static string ParseColor(string value)
    {
        var text = value.Trim();
        if (NamedColors.TryGetValue(text, out var named)) return named;
        if (!text.StartsWith('#')) text = "#" + text;
        if (!HexPattern().IsMatch(text))
            throw new ArgumentException(
                $"'{value}' is not a colour. Use #rrggbb or one of: {string.Join(", ", NamedColors.Keys)}.");
        return text.ToLowerInvariant();
    }

    /// <summary>The values a base starts from; they mirror the defaults of <c>ModulusThemeOptions</c> where a base doesn't change them.</summary>
    public static SortedDictionary<string, object> BaseValues(string baseName) => baseName.ToLowerInvariant() switch
    {
        "tabler" => new()
        {
            ["PrimaryColor"] = "#066fd1",
            ["SuccessColor"] = "#2fb344",
            ["WarningColor"] = "#f59f00",
            ["ErrorColor"] = "#d63939",
            ["BorderRadius"] = 6,
            ["Base"] = "Gray",
            ["Font"] = "Sans",
            ["Density"] = "Compact",
            ["Layout"] = "Horizontal",
        },
        "minimal" => new()
        {
            ["PrimaryColor"] = "#1f2937",
            ["SuccessColor"] = "#2f9e44",
            ["WarningColor"] = "#e8890c",
            ["ErrorColor"] = "#c92a2a",
            ["BorderRadius"] = 4,
            ["Base"] = "Neutral",
            ["Font"] = "Sans",
            ["Density"] = "Comfortable",
            ["Layout"] = "Vertical",
        },
        _ => throw new ArgumentException($"Unknown base theme '{baseName}'. Choose one of: {string.Join(", ", BaseNames)}."),
    };

    public static UiTheme Create(string name, string baseName, string? primaryColor)
    {
        ValidateName(name);
        var values = BaseValues(baseName);
        if (primaryColor is not null)
            values["PrimaryColor"] = ParseColor(primaryColor);
        return new UiTheme(name, baseName.ToLowerInvariant(), values);
    }

    // ── Storage ──────────────────────────────────────────────────────

    public static string ThemesDir(string projectDir) => Path.Combine(projectDir, FolderName);

    public static string ManifestPath(string projectDir, string name) =>
        Path.Combine(ThemesDir(projectDir), name, ManifestFile);

    public static string Serialize(UiTheme theme)
    {
        var node = new JsonObject
        {
            ["name"] = theme.Name,
            ["base"] = theme.Base,
            ["theme"] = ValuesToJson(theme.Values),
        };
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    public static UiTheme Load(string projectDir, string name)
    {
        ValidateName(name);
        var path = ManifestPath(projectDir, name);
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"Theme '{name}' not found ({FolderName}/{name}/{ManifestFile}). Run 'modulus ui theme list' to see the themes, or 'modulus ui theme create {name}'.");
        return Parse(File.ReadAllText(path), name);
    }

    public static UiTheme Parse(string json, string fallbackName)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException ex) { throw new InvalidOperationException($"Theme '{fallbackName}' is not valid JSON: {ex.Message}"); }

        var theme = root?["theme"] as JsonObject
            ?? throw new InvalidOperationException($"Theme '{fallbackName}' has no \"theme\" object.");

        var values = new SortedDictionary<string, object>();
        foreach (var (key, node) in theme)
        {
            if (node is JsonValue v)
            {
                if (v.TryGetValue<int>(out var i)) values[key] = i;
                else if (v.TryGetValue<string>(out var s)) values[key] = s;
            }
        }

        if (!values.TryGetValue("PrimaryColor", out var primary) || primary is not string p)
            throw new InvalidOperationException($"Theme '{fallbackName}' needs a PrimaryColor.");
        values["PrimaryColor"] = ParseColor(p);

        return new UiTheme(
            root?["name"]?.GetValue<string>() ?? fallbackName,
            root?["base"]?.GetValue<string>() ?? "tabler",
            values);
    }

    /// <summary>The themes saved in the app, by name.</summary>
    public static IReadOnlyList<UiTheme> List(string projectDir)
    {
        var dir = ThemesDir(projectDir);
        if (!Directory.Exists(dir)) return [];
        var themes = new List<UiTheme>();
        foreach (var sub in Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(sub);
            if (File.Exists(Path.Combine(sub, ManifestFile)))
                themes.Add(Load(projectDir, name));
        }
        return themes;
    }

    // ── appsettings.json ─────────────────────────────────────────────

    /// <summary>Merges the theme's values into <c>Modulus:Theme</c> (other keys stay) and records the name in <c>Modulus:ActiveTheme</c>.</summary>
    public static string ApplyToAppSettings(string appSettingsJson, UiTheme theme)
    {
        JsonObject root;
        try { root = JsonNode.Parse(appSettingsJson, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? new JsonObject(); }
        catch (JsonException ex) { throw new InvalidOperationException($"appsettings.json is not valid JSON: {ex.Message}"); }

        var modulus = root["Modulus"] as JsonObject ?? new JsonObject();
        var themeSection = modulus["Theme"] as JsonObject ?? new JsonObject();
        foreach (var (key, value) in ValuesToJson(theme.Values))
            themeSection[key] = value?.DeepClone();
        modulus["Theme"] = themeSection;
        modulus[ActiveKey] = theme.Name;
        root["Modulus"] = modulus;

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>The name recorded by <c>theme set</c>, or null.</summary>
    public static string? ActiveTheme(string appSettingsPath)
    {
        if (!File.Exists(appSettingsPath)) return null;
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(appSettingsPath),
                documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return root?["Modulus"]?[ActiveKey]?.GetValue<string>();
        }
        catch (JsonException) { return null; }
    }

    // ── Export ───────────────────────────────────────────────────────

    public static string Export(UiTheme theme, string format) => format.ToLowerInvariant() switch
    {
        "css" => ToCss(theme),
        "scss" => ToScss(theme),
        "tailwind" => ToTailwind(theme),
        "json" => Serialize(theme),
        _ => throw new ArgumentException($"Unknown format '{format}'. Choose one of: {string.Join(", ", ExportFormats)}."),
    };

    public static string ExportExtension(string format) => format.ToLowerInvariant() switch
    {
        "tailwind" => "tailwind.js",
        var f => f,
    };

    private static IEnumerable<(string Name, string Value)> Tokens(UiTheme theme)
    {
        var primary = theme.PrimaryColor;
        yield return ("primary", primary);
        yield return ("primary-hover", Mix(primary, "#ffffff", 0.15));
        yield return ("primary-active", Mix(primary, "#000000", 0.15));
        yield return ("primary-bg", Mix(primary, "#ffffff", 0.9));
        yield return ("success", Text(theme, "SuccessColor", "#2fb344"));
        yield return ("warning", Text(theme, "WarningColor", "#f59f00"));
        yield return ("error", Text(theme, "ErrorColor", "#d63939"));
        yield return ("radius", $"{Number(theme, "BorderRadius", 6)}px");
    }

    private static string Text(UiTheme theme, string key, string fallback) =>
        theme.Values.TryGetValue(key, out var v) && v is string s ? s : fallback;

    private static int Number(UiTheme theme, string key, int fallback) =>
        theme.Values.TryGetValue(key, out var v) && v is int i ? i : fallback;

    private static string ToCss(UiTheme theme)
    {
        var sb = new StringBuilder();
        sb.Append("/* Theme '").Append(theme.Name).Append("' (base ").Append(theme.Base).AppendLine(") */");
        sb.AppendLine(":root {");
        foreach (var (name, value) in Tokens(theme))
            sb.Append("  --m-").Append(name).Append(": ").Append(value).AppendLine(";");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string ToScss(UiTheme theme)
    {
        var sb = new StringBuilder();
        sb.Append("// Theme '").Append(theme.Name).Append("' (base ").Append(theme.Base).AppendLine(")");
        foreach (var (name, value) in Tokens(theme))
            sb.Append("$m-").Append(name).Append(": ").Append(value).AppendLine(";");
        return sb.ToString();
    }

    private static string ToTailwind(UiTheme theme)
    {
        var tokens = Tokens(theme).ToDictionary(t => t.Name, t => t.Value);
        var sb = new StringBuilder();
        sb.Append("// Theme '").Append(theme.Name).Append("' (base ").Append(theme.Base).AppendLine(")");
        sb.AppendLine("module.exports = {");
        sb.AppendLine("  theme: {");
        sb.AppendLine("    extend: {");
        sb.AppendLine("      colors: {");
        sb.AppendLine("        primary: {");
        sb.Append("          DEFAULT: '").Append(tokens["primary"]).AppendLine("',");
        sb.Append("          hover: '").Append(tokens["primary-hover"]).AppendLine("',");
        sb.Append("          active: '").Append(tokens["primary-active"]).AppendLine("',");
        sb.Append("          bg: '").Append(tokens["primary-bg"]).AppendLine("',");
        sb.AppendLine("        },");
        sb.Append("        success: '").Append(tokens["success"]).AppendLine("',");
        sb.Append("        warning: '").Append(tokens["warning"]).AppendLine("',");
        sb.Append("        error: '").Append(tokens["error"]).AppendLine("',");
        sb.AppendLine("      },");
        sb.Append("      borderRadius: { DEFAULT: '").Append(tokens["radius"]).AppendLine("' },");
        sb.AppendLine("    },");
        sb.AppendLine("  },");
        sb.AppendLine("};");
        return sb.ToString();
    }

    /// <summary>Mixes <paramref name="color"/> toward <paramref name="other"/> by <paramref name="amount"/> (0 = color, 1 = other).</summary>
    internal static string Mix(string color, string other, double amount)
    {
        var (r1, g1, b1) = Rgb(color);
        var (r2, g2, b2) = Rgb(other);
        int Channel(int a, int b) => (int)Math.Round(a + (b - a) * amount);
        return $"#{Channel(r1, r2):x2}{Channel(g1, g2):x2}{Channel(b1, b2):x2}";
    }

    private static (int R, int G, int B) Rgb(string hex) => (
        int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static JsonObject ValuesToJson(SortedDictionary<string, object> values)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in values)
            obj[key] = value switch { int i => JsonValue.Create(i), string s => JsonValue.Create(s), _ => JsonValue.Create(value.ToString()) };
        return obj;
    }
}
