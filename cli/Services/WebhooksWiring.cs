using System.Text.Json;
using System.Text.RegularExpressions;

namespace Modulus.Cli.Services;

/// <summary>
/// The edits <c>modulus add-webhooks</c> makes to existing files of the API host: Program.cs (the store module,
/// <c>AddModulusWebhooks</c> with one <c>AddEvent</c> per exposed integration event, the Admin grant and
/// <c>MapModulusWebhooks</c>), the project reference to the store module, and the settings. Every edit is idempotent and
/// keeps the file's own line endings; re-running adds events created since. An edit whose anchor is missing leaves the
/// file alone and the command prints what to add by hand.
/// </summary>
internal static partial class WebhooksWiring
{
    /// <summary>An integration event found in a module's <c>Application/IntegrationEvents</c>.</summary>
    /// <param name="Name">The <c>[IntegrationEventName]</c>.</param>
    /// <param name="TypeName">The record or class name.</param>
    /// <param name="Namespace">The file's namespace.</param>
    /// <param name="IdOnly">The event's only parameter is <c>Guid Id</c> (the shape <c>generate-crud</c> emits).</param>
    internal sealed record IntegrationEventInfo(string Name, string TypeName, string Namespace, bool IdOnly = false);

    /// <summary>The <c>[IntegrationEventName]</c> events declared under every module's <c>*.Application/IntegrationEvents</c>.</summary>
    public static IReadOnlyList<IntegrationEventInfo> FindIntegrationEvents(string solutionDir)
    {
        var modules = Path.Combine(solutionDir, "src", "Modules");
        if (!Directory.Exists(modules))
            return [];

        var events = new List<IntegrationEventInfo>();
        foreach (var file in Directory.EnumerateFiles(modules, "*.cs", SearchOption.AllDirectories)
                     .Where(f => Path.GetFileName(Path.GetDirectoryName(f)) == "IntegrationEvents"
                                 && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            events.AddRange(ParseIntegrationEvents(File.ReadAllText(file)));
        }

        return events.DistinctBy(e => e.Name, StringComparer.OrdinalIgnoreCase).OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
    }

    public static IEnumerable<IntegrationEventInfo> ParseIntegrationEvents(string source)
    {
        var ns = FileNamespace().Match(source);
        if (!ns.Success)
            yield break;
        foreach (Match m in NamedEvent().Matches(source))
        {
            var typeName = m.Groups[2].Value;
            var idOnly = Regex.IsMatch(source, $@"\b{typeName}\(\s*Guid\s+Id\s*\)");
            yield return new IntegrationEventInfo(m.Groups[1].Value, typeName, ns.Groups[1].Value, idOnly);
        }
    }

    /// <summary>
    /// Program.cs: the store module in <c>AddModulus</c>, <c>AddModulusWebhooks</c> before <c>Build()</c> (with every event
    /// not yet added), the Admin grant when the host has the Admin role, and <c>MapModulusWebhooks()</c> after the endpoints.
    /// </summary>
    public static string EnsureApiProgram(string program, string webhooksNamespace, IReadOnlyList<IntegrationEventInfo> events, string? adminRole)
    {
        var nl = NewLine(program);
        var text = program;

        // Store module, registered last in the AddModulus callback.
        if (!text.Contains("AddModule<WebhooksModule>()", StringComparison.Ordinal))
        {
            var call = text.IndexOf("AddModulus(", StringComparison.Ordinal);
            var close = call < 0 ? -1 : text.IndexOf("});", call, StringComparison.Ordinal);
            if (close < 0)
                return program;
            var lineStart = text.LastIndexOf('\n', close) + 1;
            var lastRegistration = text.LastIndexOf("modules.AddModule<", close, StringComparison.Ordinal);
            var indent = "    ";
            if (lastRegistration > call)
            {
                var regLine = text.LastIndexOf('\n', lastRegistration) + 1;
                indent = text[regLine..lastRegistration];
            }

            text = text[..lineStart] + indent + "modules.AddModule<WebhooksModule>();" + nl + text[lineStart..];
        }

        // AddModulusWebhooks with the events.
        if (!text.Contains("AddModulusWebhooks(", StringComparison.Ordinal))
        {
            var build = text.IndexOf("var app = builder.Build();", StringComparison.Ordinal);
            if (build < 0)
                return program;
            var block =
                "// ── Webhooks ───────────────────────────────────────────────────" + nl +
                "// Integration events subscribers can receive (modulus add-webhooks; re-run it to add events" + nl +
                "// created since). Each is POSTed, signed per Standard Webhooks, to the matching subscriptions" + nl +
                "// of the event's tenant, with retries. Settings live under \"Webhooks\"; manage subscriptions" + nl +
                "// at /api/webhooks (permission webhooks:manage)." + nl +
                "builder.Services.AddModulusWebhooks(builder.Configuration, webhooks =>" + nl +
                "{" + nl +
                "});" + nl;
            if (adminRole is not null)
                block += $"builder.Services.AddPermissionGrants(grants => grants.GrantToRole(\"{adminRole}\", \"webhooks:manage\"));" + nl;
            text = text[..build] + block + nl + text[build..];
        }

        text = EnsureEvents(text, events);

        if (!text.Contains("MapModulusWebhooks(", StringComparison.Ordinal))
        {
            var block = "// Webhook management API (subscriptions, deliveries, test and retry), behind webhooks:manage." + nl +
                        "app.MapModulusWebhooks();" + nl;
            var endpoints = text.IndexOf("app.MapModulusEndpoints(", StringComparison.Ordinal);
            var end = endpoints < 0 ? -1 : text.IndexOf(".ToArray());", endpoints, StringComparison.Ordinal);
            var lineEnd = end < 0 ? -1 : text.IndexOf('\n', end);
            if (lineEnd >= 0)
            {
                text = text[..(lineEnd + 1)] + nl + block + text[(lineEnd + 1)..];
            }
            else
            {
                var run = text.IndexOf("app.Run();", StringComparison.Ordinal);
                if (run < 0)
                    return program;
                text = text[..run] + block + nl + text[run..];
            }
        }

        text = EnsureUsing(text, "using Modulus.Webhooks;");
        text = EnsureUsing(text, $"using {webhooksNamespace}.Infrastructure;");
        foreach (var ns in events.Select(e => e.Namespace).Distinct(StringComparer.Ordinal))
            text = EnsureUsing(text, $"using {ns};");
        return text;
    }

    // Adds `webhooks.AddEvent<T>();` for each event not yet in the AddModulusWebhooks callback (its `{ ... });` body).
    private static string EnsureEvents(string text, IReadOnlyList<IntegrationEventInfo> events)
    {
        var call = text.IndexOf("AddModulusWebhooks(", StringComparison.Ordinal);
        if (call < 0)
            return text;
        var open = text.IndexOf("webhooks =>", call, StringComparison.Ordinal);
        var close = open < 0 ? -1 : text.IndexOf("});", open, StringComparison.Ordinal);
        if (close < 0)
            return text;

        var nl = NewLine(text);
        // Scoped to this callback's variable: a realtime.AddEvent<T> of the same event is not a webhook registration.
        var missing = events.Where(e => !text.Contains($"webhooks.AddEvent<{e.TypeName}>", StringComparison.Ordinal)).ToList();
        if (missing.Count == 0)
            return text;

        var lineStart = text.LastIndexOf('\n', close) + 1;
        var lines = string.Concat(missing.Select(e => $"    webhooks.AddEvent<{e.TypeName}>();{nl}"));
        return text[..lineStart] + lines + text[lineStart..];
    }

    /// <summary>The API project references the store module's Infrastructure project.</summary>
    public static string EnsureApiProjectReference(string csproj, string relativePath)
    {
        if (csproj.Contains(relativePath, StringComparison.Ordinal))
            return csproj;
        var end = csproj.LastIndexOf("</Project>", StringComparison.Ordinal);
        if (end < 0)
            return csproj;
        var nl = NewLine(csproj);
        var head = csproj[..end].TrimEnd();
        return head + nl + nl +
               $"  <ItemGroup>{nl}" +
               $"    <ProjectReference Include=\"{relativePath}\" />{nl}" +
               $"  </ItemGroup>{nl}" + nl + csproj[end..];
    }

    /// <summary>
    /// <c>appsettings.json</c> gets the <c>Webhooks</c> connection string (when it has a <c>ConnectionStrings</c> section) and a
    /// <c>Webhooks</c> section; the Development file allows http and local endpoints, the Testing file turns delivery off.
    /// </summary>
    public static string EnsureSettings(string json, string environment, string? connectionString)
    {
        var text = json;
        if (connectionString is not null)
            text = EnsureConnectionString(text, "Webhooks", connectionString);

        IReadOnlyList<string> body = environment switch
        {
            "Development" =>
            [
                "\"AllowHttp\": true,",
                "\"AllowPrivateNetworks\": true",
            ],
            "Testing" => ["\"EnableDelivery\": false"],
            _ =>
            [
                "\"EnableDelivery\": true,",
                "\"AllowHttp\": false,",
                "\"AllowPrivateNetworks\": false",
            ],
        };
        return EnsureTopLevelSection(text, "Webhooks", body);
    }

    internal static string EnsureConnectionString(string json, string name, string value)
    {
        var match = ConnectionStringsSection().Match(json);
        if (!match.Success || HasConnectionString(json, name))
            return json;

        var nl = NewLine(json);
        var after = match.Index + match.Length;
        var close = json.IndexOf('}', after);
        var isEmpty = close >= 0 && json[after..close].Trim().Length == 0;
        var entry = $"    \"{name}\": {JsonSerializer.Serialize(value)}{(isEmpty ? string.Empty : ",")}";
        var lineEnd = json.IndexOf('\n', after);
        if (lineEnd < 0)
            return json;
        var result = json[..(lineEnd + 1)] + entry + nl + json[(lineEnd + 1)..];
        return IsJsonObject(result) ? result : json;
    }

    private static bool HasConnectionString(string json, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(json, JsonOptions);
            return document.RootElement.TryGetProperty("ConnectionStrings", out var section)
                   && section.ValueKind == JsonValueKind.Object
                   && section.TryGetProperty(name, out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static string EnsureTopLevelSection(string json, string section, IReadOnlyList<string> bodyLines)
    {
        if (HasTopLevelSection(json, section) || !IsJsonObject(json))
            return json;
        var close = json.LastIndexOf('}');
        if (close < 0)
            return json;

        var nl = NewLine(json);
        var head = json[..close].TrimEnd();
        var comma = head.EndsWith('{') ? string.Empty : ",";
        var body = string.Concat(bodyLines.Select(l => "    " + l + nl));
        var result = $"{head}{comma}{nl}  \"{section}\": {{{nl}{body}  }}{nl}{json[close..]}";
        return IsJsonObject(result) ? result : json;
    }

    private static readonly JsonDocumentOptions JsonOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static bool HasTopLevelSection(string json, string section)
    {
        try
        {
            using var document = JsonDocument.Parse(json, JsonOptions);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.EnumerateObject().Any(p => string.Equals(p.Name, section, StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsJsonObject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, JsonOptions);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Adds the using after the file's leading using block (only usings, comments and blank lines before it).
    internal static string EnsureUsing(string text, string usingLine)
    {
        if (text.Contains(usingLine, StringComparison.Ordinal))
            return text;
        var nl = NewLine(text);
        var insertAt = 0;
        var position = 0;
        while (position < text.Length)
        {
            var lineEnd = text.IndexOf('\n', position);
            var next = lineEnd < 0 ? text.Length : lineEnd + 1;
            var line = text[position..next].Trim();
            if (line.StartsWith("using ", StringComparison.Ordinal) && line.EndsWith(';')
                && !line.StartsWith("using var ", StringComparison.Ordinal) && !line.Contains('(', StringComparison.Ordinal))
            {
                insertAt = next;
            }
            else if (line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal))
            {
                break;
            }

            position = next;
        }

        return text[..insertAt] + usingLine + nl + text[insertAt..];
    }

    internal static string NewLine(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    [GeneratedRegex(@"^namespace\s+([\w.]+)\s*;", RegexOptions.Multiline)]
    private static partial Regex FileNamespace();

    [GeneratedRegex(@"\[IntegrationEventName\(""([^""]+)""\)\]\s*(?:\[[^\]]*\]\s*)*public\s+(?:sealed\s+)?(?:partial\s+)?(?:record|class)\s+(\w+)")]
    private static partial Regex NamedEvent();

    [GeneratedRegex(@"^  ""ConnectionStrings""\s*:\s*\{[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex ConnectionStringsSection();
}
