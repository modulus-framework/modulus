using System.Text.Json;
using System.Text.Json.Nodes;

namespace Modulus.Cli.Services;

/// <summary>
/// Wiring for the embedded AI assistant: <c>Ai:Host</c> settings (no secret), the two <c>Program.cs</c> calls and the layout partial.
/// Pure text transforms, idempotent, so they are unit-testable.
/// </summary>
internal static class UiAssistant
{
    public const string Permission = "ai:use";
    public const string PartialCall = "<partial name=\"_AiAssistant\" />";

    /// <summary>Adds <c>Ai:Host</c> (disabled, no key) and keeps every other setting.</summary>
    public static string EnsureSettings(string appSettingsJson)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(appSettingsJson,
                documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? new JsonObject();
        }
        catch (JsonException ex) { throw new InvalidOperationException($"appsettings.json is not valid JSON: {ex.Message}"); }

        var ai = root["Ai"] as JsonObject ?? new JsonObject();
        if (ai["Host"] is not null)
            return appSettingsJson;
        ai["Host"] = new JsonObject { ["Enabled"] = false, ["BaseUrl"] = "", ["SdkScript"] = "/lib/ai-sdk/ai-assistant.js", ["Permission"] = Permission };
        root["Ai"] = ai;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>Adds <c>AddAiAssistant</c> before the app is built and <c>MapAiAssistant</c> before it runs.</summary>
    public static string EnsureProgram(string program, string uiNamespace)
    {
        var nl = program.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        if (!program.Contains("AddAiAssistant(", StringComparison.Ordinal))
            program = InsertBefore(program, "var app = builder.Build();", "builder.Services.AddAiAssistant(builder.Configuration);", nl);
        if (!program.Contains("MapAiAssistant(", StringComparison.Ordinal))
            program = InsertBefore(program, "app.Run();", "app.MapAiAssistant();", nl);
        var using_ = $"using {uiNamespace}.Ai;";
        if (!program.Contains(using_, StringComparison.Ordinal))
            program = using_ + nl + program;
        return program;
    }

    /// <summary>Renders the partial just before <c>&lt;/body&gt;</c>; null when the layout has none to anchor on.</summary>
    public static string? EnsureLayout(string layout)
    {
        if (layout.Contains("_AiAssistant", StringComparison.Ordinal))
            return layout;
        var anchor = layout.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return anchor < 0 ? null : layout.Insert(anchor, PartialCall + (layout.Contains("\r\n") ? "\r\n" : "\n"));
    }

    private static string InsertBefore(string text, string anchor, string line, string nl)
    {
        var at = text.IndexOf(anchor, StringComparison.Ordinal);
        if (at < 0)
            throw new InvalidOperationException($"Could not find '{anchor}' in Program.cs. Add `{line}` yourself.");
        return text.Insert(at, line + nl + (anchor.StartsWith("var app") ? nl : ""));
    }
}
