using System.Text.Json;
using System.Text.RegularExpressions;

namespace Modulus.Cli.Services;

/// <summary>
/// The edits <c>modulus generate-graphql</c> makes to existing files: the Presentation project's package reference, the API
/// host's Program.cs (<c>AddModulusGraphQL</c> over the module Presentation assemblies, <c>MapModulusGraphQL</c>), its
/// settings, and a BFF's <c>/graphql</c> passthrough route. Every edit is idempotent and keeps the file's line endings; an
/// edit whose anchor is missing leaves the file alone.
/// </summary>
internal static partial class GraphQLWiring
{
    public const string PackageId = "Synvro.Modulus.GraphQL";

    /// <summary>The Presentation project references Modulus.GraphQL (GraphQL.NET comes with it).</summary>
    public static string EnsurePresentationProject(string csproj, string frameworkVersion)
    {
        if (csproj.Contains(PackageId, StringComparison.OrdinalIgnoreCase))
            return csproj;
        var nl = GrpcWiring.NewLine(csproj);
        return GrpcWiring.InsertBeforeProjectEnd(csproj,
            $"  <!-- GraphQL (modulus generate-graphql): the module's fields of the one schema, under GraphQL/. -->{nl}" +
            $"  <ItemGroup>{nl}" +
            $"    <PackageReference Include=\"{PackageId}\" Version=\"{frameworkVersion}\" />{nl}" +
            $"  </ItemGroup>{nl}");
    }

    /// <summary>
    /// The optional features' services (<c>Configuration/FeatureServicesSetup.cs</c>): the schema over the module Presentation
    /// assemblies.
    /// </summary>
    public static string EnsureApiServices(string text, string rootNamespace)
    {
        var nl = GrpcWiring.NewLine(text);
        if (!text.Contains("AddModulusGraphQL(", StringComparison.Ordinal))
        {
            var block =
                "        // ── GraphQL ────────────────────────────────────────────────────" + nl +
                "        // One schema at /graphql made of the fields each module's Presentation assembly contributes" + nl +
                "        // (IGraphQLContributor; modulus generate-graphql). Resolvers go through the mediator, fields carry" + nl +
                "        // the same permissions as the HTTP endpoints, and depth/complexity limits apply. Introspection and the" + nl +
                "        // GraphiQL IDE (/graphql/ui) are on only where \"GraphQL\" settings say so (Development)." + nl +
                "        builder.Services.AddModulusGraphQL(builder.Configuration," + nl +
                $"            Directory.GetFiles(AppContext.BaseDirectory, \"{rootNamespace}.Modules.*.Presentation.dll\")" + nl +
                "                .Select(Assembly.LoadFrom)" + nl +
                "                .ToArray());" + nl;
            var inserted = ApiHostFiles.InsertFeatureServices(text, block, nl);
            if (inserted == text)
                return text;
            text = inserted + nl;
        }

        text = GrpcWiring.EnsureUsing(text, "using Modulus.GraphQL;");
        return GrpcWiring.EnsureUsing(text, "using System.Reflection;");
    }

    /// <summary>The optional features' endpoints (<c>Endpoints/FeatureEndpoints.cs</c>): <c>MapModulusGraphQL()</c>.</summary>
    public static string EnsureApiEndpoints(string text)
    {
        var nl = GrpcWiring.NewLine(text);
        if (!text.Contains("MapModulusGraphQL(", StringComparison.Ordinal))
        {
            var block = "        // GraphQL endpoint (POST/GET /graphql), behind the sign-in; each field checks its own permission." + nl +
                        "        app.MapModulusGraphQL();" + nl;
            var inserted = ApiHostFiles.InsertFeatureEndpoints(text, block, nl);
            if (inserted is null)
                return text;
            text = inserted;
        }

        return GrpcWiring.EnsureUsing(text, "using Modulus.GraphQL;");
    }

    /// <summary>
    /// <c>appsettings.json</c> gets a <c>GraphQL</c> section (sign-in required, introspection and the IDE off, limits); the
    /// Development file turns introspection, the IDE and exception details on.
    /// </summary>
    public static string EnsureApiSettings(string json, bool development)
        => GrpcWiring.EnsureTopLevelSection(json, "GraphQL", development
            ?
            [
                "\"EnableIntrospection\": true,",
                "\"EnableUi\": true,",
                "\"ExposeExceptionDetails\": true",
            ]
            :
            [
                "\"Path\": \"/graphql\",",
                "\"RequireAuthenticatedUser\": true,",
                "\"EnableIntrospection\": false,",
                "\"EnableUi\": false,",
                "\"MaxDepth\": 15,",
                "\"MaxComplexity\": 1000",
            ]);

    /// <summary>
    /// A BFF client's <c>RemoteApis</c> array gets <c>{ "LocalPath": "/graphql", "Service": "api" }</c>, so the BFF proxies
    /// GraphQL to the API under its own edge rules (session token, CSRF header, rate limit). An array that already routes
    /// <c>/graphql</c>, or a file without the array, is left alone.
    /// </summary>
    public static string EnsureBffRemoteApi(string json, string service = "api") => EnsureRemoteApi(json, "/graphql", service);

    /// <summary>
    /// Adds <c>{ "LocalPath": localPath, "Service": service }</c> (plus <c>"EventStream": true</c> when asked) to a BFF
    /// client's <c>RemoteApis</c> array, unless it already routes <paramref name="localPath"/> or the file has no array.
    /// </summary>
    public static string EnsureRemoteApi(string json, string localPath, string service, bool eventStream = false)
    {
        var match = RemoteApisArray().Match(json);
        if (!match.Success || match.Groups["body"].Value.Contains($"\"{localPath}\"", StringComparison.Ordinal))
            return json;

        var nl = GrpcWiring.NewLine(json);
        var body = match.Groups["body"].Value.TrimEnd();
        var indent = match.Groups["indent"].Value + "  ";
        var entry = $"{{ \"LocalPath\": \"{localPath}\", \"Service\": \"{service}\"{(eventStream ? ", \"EventStream\": true" : string.Empty)} }}";
        var newBody = body.Trim().Length == 0
            ? nl + indent + entry
            : body + "," + nl + indent + entry;
        var result = json[..match.Groups["body"].Index] + newBody + json[(match.Groups["body"].Index + match.Groups["body"].Length)..];
        return IsJson(result) ? result : json;
    }

    private static bool IsJson(string json)
    {
        try
        {
            using var _ = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    [GeneratedRegex("\"RemoteApis\"\\s*:\\s*\\[(?<body>[^\\]]*?)(?<close>\\r?\\n(?<indent>[ \\t]*)\\])")]
    private static partial Regex RemoteApisArray();
}
