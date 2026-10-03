using System.Text.Json;
using System.Text.RegularExpressions;

namespace Modulus.Cli.Services;

/// <summary>
/// The edits <c>modulus generate-grpc</c> makes to existing files: the module's Presentation project (the contract under
/// <c>Protos/</c>, compiled to server stubs), the API host (<c>AddModulusGrpc</c>/<c>MapModulusGrpc</c>, the <c>Grpc</c>
/// settings and, in Development, an HTTP/2-only endpoint) and each chosen BFF (the same contract linked as a client,
/// registered with <c>AddBffGrpcClient</c>). Every edit is idempotent and keeps the file's own line endings; an edit whose
/// anchor is missing leaves the file alone and the command prints what to add by hand.
/// </summary>
internal static partial class GrpcWiring
{
    /// <summary>
    /// The development gRPC port of the API host. Kestrel negotiates HTTP/2 over plain HTTP only on an HTTP/2-only
    /// endpoint, so gRPC gets its own next to the HTTP one (5180; the Web host is 5181, BFFs 5190+).
    /// </summary>
    public const int DevelopmentGrpcPort = 5189;

    public static string GrpcToolsVersion => ThirdPartyPackages.GetRecommendedVersion("Grpc.Tools")!;

    /// <summary>The Presentation project compiles every <c>Protos/**/*.proto</c> to server stubs and clients, and references Modulus.Grpc.</summary>
    public static string EnsurePresentationProject(string csproj, string frameworkVersion)
    {
        if (csproj.Contains("Grpc.Tools", StringComparison.OrdinalIgnoreCase))
            return csproj;
        var nl = NewLine(csproj);
        return InsertBeforeProjectEnd(csproj,
            $"  <!-- gRPC (modulus generate-grpc): the contracts under Protos/, compiled to server stubs and clients (tests, other modules). -->{nl}" +
            $"  <ItemGroup>{nl}" +
            $"    <PackageReference Include=\"Cobytelabs.Modulus.Grpc\" Version=\"{frameworkVersion}\" />{nl}" +
            $"    <PackageReference Include=\"Grpc.Tools\" Version=\"{GrpcToolsVersion}\" PrivateAssets=\"all\" />{nl}" +
            $"    <Protobuf Include=\"Protos/**/*.proto\" GrpcServices=\"Both\" ProtoRoot=\"Protos\" />{nl}" +
            $"  </ItemGroup>{nl}");
    }

    /// <summary>The API host registers the gRPC server before <c>Build()</c> and maps the modules' services after their endpoints.</summary>
    public static string EnsureApiProgram(string program, string rootNamespace)
    {
        var nl = NewLine(program);
        var text = EnsureUsing(program, "using Modulus.Grpc;");

        if (!text.Contains("AddModulusGrpc(", StringComparison.Ordinal))
        {
            var build = text.IndexOf("var app = builder.Build();", StringComparison.Ordinal);
            if (build < 0)
                return program;
            text = text[..build] +
                "// ── gRPC ───────────────────────────────────────────────────────" + nl +
                "// Module gRPC services (modulus generate-grpc): domain exceptions become status codes," + nl +
                "// grpc.health.v1 reports the module health checks, and reflection (grpcurl, Postman)" + nl +
                "// is on only where \"Grpc:EnableReflection\" is (Development). Settings live under \"Grpc\"." + nl +
                "builder.Services.AddModulusGrpc(builder.Configuration);" + nl + nl +
                text[build..];
        }

        if (!text.Contains("MapModulusGrpc(", StringComparison.Ordinal))
        {
            var block =
                "// gRPC services of the module Presentation assemblies, found like the endpoints above;" + nl +
                "// each carries its own [Authorize]. grpc.health.v1 is mapped too (anonymous)." + nl +
                "app.MapModulusGrpc(" + nl +
                $"    Directory.GetFiles(AppContext.BaseDirectory, \"{rootNamespace}.Modules.*.Presentation.dll\")" + nl +
                "        .Select(Assembly.LoadFrom)" + nl +
                "        .ToArray());" + nl;

            var endpoints = text.IndexOf("app.MapModulusEndpoints(", StringComparison.Ordinal);
            var end = endpoints < 0 ? -1 : text.IndexOf(".ToArray());", endpoints, StringComparison.Ordinal);
            if (end >= 0)
            {
                var lineEnd = text.IndexOf('\n', end);
                if (lineEnd < 0)
                    return program;
                text = text[..(lineEnd + 1)] + nl + block + text[(lineEnd + 1)..];
            }
            else
            {
                var run = text.IndexOf("app.Run();", StringComparison.Ordinal);
                if (run < 0)
                    return program;
                text = text[..run] + block + nl + text[run..];
            }

            text = EnsureUsing(text, "using System.Reflection;");
        }

        return text;
    }

    /// <summary>
    /// <c>appsettings.json</c> gets a <c>Grpc</c> section (detailed errors and reflection off); the Development file turns
    /// both on and, unless it already configures Kestrel, adds the HTTP endpoint and an HTTP/2-only gRPC endpoint.
    /// </summary>
    public static string EnsureApiSettings(string json, bool development, int httpPort)
    {
        var flag = development ? "true" : "false";
        var text = EnsureTopLevelSection(json, "Grpc",
            ["\"EnableDetailedErrors\": " + flag + ",", "\"EnableReflection\": " + flag]);
        if (development)
        {
            text = EnsureTopLevelSection(text, "Kestrel",
            [
                "\"Endpoints\": {",
                $"  \"Http\": {{ \"Url\": \"http://localhost:{httpPort}\" }},",
                $"  \"Grpc\": {{ \"Url\": \"http://localhost:{DevelopmentGrpcPort}\", \"Protocols\": \"Http2\" }}",
                "}",
            ]);
        }

        return text;
    }

    /// <summary>
    /// Pins <c>Identity:Issuer</c> in the settings' top-level <c>Identity</c> section (the generated two-space shape). A local
    /// OpenIddict server otherwise takes each request's address as the issuer, so a token issued on the HTTP endpoint is
    /// rejected on the gRPC one. A section that already sets an issuer, or a file without the section, is left alone.
    /// </summary>
    public static string EnsureIdentityIssuer(string json, string issuer)
    {
        try
        {
            using var document = JsonDocument.Parse(json, JsonOptions);
            if (!document.RootElement.TryGetProperty("Identity", out var identity) || identity.ValueKind != JsonValueKind.Object
                || identity.EnumerateObject().Any(p => string.Equals(p.Name, "Issuer", StringComparison.OrdinalIgnoreCase)))
            {
                return json;
            }
        }
        catch (JsonException)
        {
            return json;
        }

        var match = IdentitySection().Match(json);
        if (!match.Success)
            return json;
        var nl = NewLine(json);
        var open = match.Index + match.Length;
        var empty = json[open..].TrimStart().StartsWith('}');
        var result = json[..open] + nl + $"    \"Issuer\": \"{issuer}\"" + (empty ? nl + "  " : ",") + json[open..];
        return IsJsonObject(result) ? result : json;
    }

    /// <summary>True when the settings configure Kestrel endpoints (then gRPC's HTTP/2 endpoint is the app's to add).</summary>
    public static bool HasKestrelSection(string json) => HasTopLevelSection(json, "Kestrel");

    /// <summary>
    /// A BFF compiles the module's contracts as clients, linked from the module's Presentation project so there is one copy:
    /// <c>&lt;Protobuf Include="{relative}/Protos/**/*.proto" GrpcServices="Client" ... /&gt;</c>, plus Grpc.Tools.
    /// </summary>
    public static string EnsureBffProject(string csproj, string presentationRelativeDir, string module)
    {
        var include = presentationRelativeDir.Replace('\\', '/').TrimEnd('/') + "/Protos/**/*.proto";
        var nl = NewLine(csproj);
        var text = csproj;
        if (!text.Contains("Grpc.Tools", StringComparison.OrdinalIgnoreCase))
        {
            text = InsertBeforeProjectEnd(text,
                $"  <!-- gRPC clients (modulus generate-grpc): the modules' contracts, linked from their Presentation projects. -->{nl}" +
                $"  <ItemGroup>{nl}" +
                $"    <PackageReference Include=\"Grpc.Tools\" Version=\"{GrpcToolsVersion}\" PrivateAssets=\"all\" />{nl}" +
                $"  </ItemGroup>{nl}");
        }

        if (text.Contains($"Include=\"{include}\"", StringComparison.Ordinal))
            return text;

        var protoRoot = presentationRelativeDir.Replace('\\', '/').TrimEnd('/') + "/Protos";
        var line = $"    <Protobuf Include=\"{include}\" GrpcServices=\"Client\" ProtoRoot=\"{protoRoot}\" Link=\"Protos/{module}/%(RecursiveDir)%(Filename)%(Extension)\" />";
        var tools = text.IndexOf("Include=\"Grpc.Tools\"", StringComparison.OrdinalIgnoreCase);
        var groupEnd = tools < 0 ? -1 : text.IndexOf("</ItemGroup>", tools, StringComparison.Ordinal);
        if (groupEnd < 0)
            return text;
        var lineStart = text.LastIndexOf('\n', groupEnd) + 1;
        return text[..lineStart] + line + nl + text[lineStart..];
    }

    /// <summary>Adds <c>services.AddBffGrpcClient&lt;{client}&gt;("{service}");</c> to the BFF's <c>ApiClientRegistration</c>.</summary>
    public static string EnsureBffRegistration(string registration, string clientType, string service, string clientNamespace)
    {
        if (registration.Contains($"AddBffGrpcClient<{clientType}>", StringComparison.Ordinal))
            return registration;
        const string anchor = "        return services;";
        var index = registration.IndexOf(anchor, StringComparison.Ordinal);
        if (index < 0)
            return registration;
        var nl = NewLine(registration);
        var text = registration[..index] + $"        services.AddBffGrpcClient<{clientType}>(\"{service}\");" + nl + registration[index..];
        return EnsureUsing(text, $"using {clientNamespace};");
    }

    /// <summary>
    /// Sets <c>Bff:Services:{service}:GrpcAddress</c> in a BFF's settings when the service entry has the generated
    /// single-line shape <c>"{service}": { "Address": "..." }</c>; anything else is left alone.
    /// </summary>
    public static string EnsureBffGrpcAddress(string json, string service, string grpcAddress)
    {
        var pattern = new Regex("(\"" + Regex.Escape(service) + "\"\\s*:\\s*\\{\\s*\"Address\"\\s*:\\s*\"[^\"]*\")(\\s*\\})");
        var match = pattern.Match(json);
        if (!match.Success)
            return json;
        return json[..match.Index] + match.Groups[1].Value + $", \"GrpcAddress\": \"{grpcAddress}\"" + match.Groups[2].Value + json[(match.Index + match.Length)..];
    }

    /// <summary>The <c>applicationUrl</c> port of a project's <c>Properties/launchSettings.json</c>, or null.</summary>
    public static int? ReadLaunchPort(string projectDir)
    {
        var launch = Path.Combine(projectDir, "Properties", "launchSettings.json");
        if (!File.Exists(launch))
            return null;
        var match = PortPattern().Match(File.ReadAllText(launch));
        return match.Success ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
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

    internal static string InsertBeforeProjectEnd(string csproj, string block)
    {
        var end = csproj.LastIndexOf("</Project>", StringComparison.Ordinal);
        if (end < 0)
            return csproj;
        var nl = NewLine(csproj);
        var head = csproj[..end].TrimEnd();
        return head + nl + nl + block + nl + csproj[end..];
    }

    // Adds the using after the file's leading using block (only usings, comments and blank lines before it), or at the
    // top when there is none. A `using var x = ...;` statement is not a directive and ends the block.
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

    [GeneratedRegex(@"^  ""Identity""\s*:\s*\{", RegexOptions.Multiline)]
    private static partial Regex IdentitySection();

    [GeneratedRegex(@"""applicationUrl""\s*:\s*""https?://[^:""]+:(\d+)")]
    private static partial Regex PortPattern();
}
