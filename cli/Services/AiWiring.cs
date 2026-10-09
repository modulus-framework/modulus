using System.Text.RegularExpressions;

namespace Modulus.Cli.Services;

/// <summary>
/// The edits <c>modulus add-ai</c> and <c>modulus generate-crud --ai</c> make to existing files: the host's Program.cs
/// (<c>AddModulusAiConnector</c>, <c>MapModulusAiConnector</c>, the indexing identity's grants), its settings, and the AI
/// attributes on a CRUD set (entity, list query, lookup query). Every edit is idempotent and keeps the file's line endings.
/// </summary>
internal static partial class AiWiring
{
    public const string ConnectorPackageId = "Cobytelabs.Modulus.AI.Connector";
    public const string ConnectorEfPackageId = "Cobytelabs.Modulus.AI.Connector.EntityFrameworkCore";

    /// <summary>The conformance kit (fake platform + suite) the generated tests use.</summary>
    public const string ConnectorTestingPackageId = "Cobytelabs.Modulus.AI.Connector.Testing";

    /// <summary>The role the connector's indexing identity carries (<c>Ai:Connector:Indexing:Roles</c>).</summary>
    public const string IndexerRole = "AiIndexer";

    /// <summary>The platform API key the generated tests present (only its hash is configured, in appsettings.Testing.json).</summary>
    public const string TestApiKey = "modulus-test-ai-platform-key";

    /// <summary>SHA-256 (hex) of <see cref="TestApiKey"/>, as <c>AiApiKeys.Hash</c> computes it.</summary>
    public const string TestApiKeyHash = "a54a73337cf9dc911f953741c58678e2c22fb26998e694410686c39cb4118b36";

    /// <summary>A placeholder RSA public key (JWKS) for the tests' throwaway platform; no private key exists for it.</summary>
    public const string TestSigningKeys =
        "{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"test\",\"use\":\"sig\",\"alg\":\"RS256\",\"e\":\"AQAB\",\"n\":\"" +
        "ugjuvRI2OCvP8nz13cjziHd_zX3Kel-SMqjptLbxLKI9xYFCQQ8Zsg3C49-uI8Bw4oV0wChubOBfdUHSnRhtWQqKMaGKAPOM5wVp350XZpH_34tNSZj2bHX70Zlm" +
        "BpKkpxG2lgztiSC43aN1mP6dw4wqUilFVAgWpHru0VTQYmcajBpznbX9MX5dMtWBJNkckq5L4a3SmFaMOMLqUEtuG0z3BH4YAcuGieiNrKF08j4KL7wIJcnZsh9r" +
        "jMIXB2NMBvCfw5zjaGBONT-uu8ZNVbLiZIHuqssz9tLyedh2JzBFJciz25Qpm0HkJKFbpxhAjO4vrsQrRtwzBkhcKyzQww\"}]}";

    /// <summary>The app instance the generated tests extract as.</summary>
    public const string TestInstance = "test-instance";

    /// <summary>True when the host already hosts the connector.</summary>
    public static bool HasConnector(string program) => program.Contains("AddModulusAiConnector(", StringComparison.Ordinal);

    /// <summary>True when the host has the local identity backend, whose accounts the connector resolves envelope users against.</summary>
    public static bool HasIdentityUsers(string program) =>
        program.Contains("AddModulusOpenIddict(", StringComparison.Ordinal)
        && program.Contains("AddModule<IdentityModule>", StringComparison.Ordinal);

    /// <summary>
    /// Program.cs: <c>AddModulusAiConnector</c> before <c>Build()</c> (with <c>UseIdentityUsers</c> when the host has the local
    /// identity backend, and the EF journal) and <c>MapModulusAiConnector()</c> after the endpoints (else before <c>app.Run()</c>).
    /// </summary>
    public static string EnsureConnectorProgram(string program)
    {
        var nl = WebhooksWiring.NewLine(program);
        var text = program;
        var identity = HasIdentityUsers(program);

        if (!HasConnector(text))
        {
            var build = text.IndexOf("var app = builder.Build();", StringComparison.Ordinal);
            if (build < 0)
                return program;

            var users = identity
                ? "    .UseIdentityUsers<ModulusUser>(user => user.IsActive)    // the envelope's user, matched to an account" + nl
                : "    // .UseUserResolver<MyResolver>()   // no local accounts: without a resolver every user call is refused" + nl;
            text = text[..build] +
                "// ── AI connector ───────────────────────────────────────────────" + nl +
                "// Hosts the AI platform's connector wire contract v1 (modulus add-ai): /_ai/connector/* (manifest, capabilities," + nl +
                "// record lookups, authorization checks, extraction and the change feed). The platform calls it with an API key" + nl +
                "// (Ai:Connector:ApiKeyHashes keeps only SHA-256 hashes) plus a signed envelope naming the user, and each call runs" + nl +
                "// as that user with their own permissions; nothing is ever written. Expose data with [AiCapability]/[AiResource] on" + nl +
                "// queries and [AiIndexed]/[AiQueryable] on entities (modulus generate-crud <Entity> --ai). The ai_changes journal is" + nl +
                "// a new table in every module database: add a migration (modulus migrate add AiChanges)." + nl +
                "builder.Services.AddModulusAiConnector(builder.Configuration, ai => ai" + nl +
                users +
                "    .UseEntityFrameworkCore());                                  // change journal, change feed, entity source" + nl + nl +
                text[build..];
        }

        if (!text.Contains("MapModulusAiConnector(", StringComparison.Ordinal))
        {
            var block = "// AI connector endpoints (/_ai/connector/*), each behind the platform's API key." + nl +
                        "app.MapModulusAiConnector();" + nl;
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

        text = WebhooksWiring.EnsureUsing(text, "using Modulus.AI.Connector;");
        text = WebhooksWiring.EnsureUsing(text, "using Modulus.AI.Connector.EntityFrameworkCore;");
        if (identity)
            text = WebhooksWiring.EnsureUsing(text, "using Modulus.Identity.Abstractions;");
        return text;
    }

    /// <summary>
    /// Program.cs: grants <paramref name="permission"/> to the indexing identity's role, so <c>/extract</c> may read the
    /// entity. Only on a host that seeds grants (<c>AddPermissionGrants</c>); otherwise the app grants it its own way.
    /// </summary>
    public static string EnsureIndexerGrant(string program, string permission)
    {
        var marker = $"GrantToRole(\"{IndexerRole}\", \"{permission}\")";
        if (program.Contains(marker, StringComparison.Ordinal) || !program.Contains("AddPermissionGrants(", StringComparison.Ordinal))
            return program;

        var build = program.IndexOf("var app = builder.Build();", StringComparison.Ordinal);
        if (build < 0)
            return program;

        var nl = WebhooksWiring.NewLine(program);
        var line = $"builder.Services.AddPermissionGrants(grants => grants.{marker}); // the AI index may read it" + nl;
        // After the last existing grant line, so the seed stays in one place.
        var last = program.LastIndexOf("builder.Services.AddPermissionGrants(", build, StringComparison.Ordinal);
        var at = last < 0 ? build : program.IndexOf('\n', last) + 1;
        return at <= 0 ? program : program[..at] + line + (last < 0 ? nl : string.Empty) + program[at..];
    }

    /// <summary>
    /// <c>appsettings.json</c> gets <c>Ai:Connector</c>, turned off: until the app is registered with the platform (instances,
    /// issuer, keys URL, the hash of the platform's key, and the platform key for revocations from a secret store) the
    /// endpoints answer <c>404</c>, and startup validation, which refuses an incomplete connector, is skipped.
    /// </summary>
    public static string EnsureConnectorSettings(string json, string appName)
        => EnsureAiSubsection(json, "Connector",
        [
            "\"Enabled\": false,",
            "\"AppType\": \"erp\",",
            $"\"AppName\": \"{appName}\",",
            "\"ApiKeyHashes\": [],",
            "\"Platform\": { \"Issuer\": null, \"JwksUrl\": null, \"BaseUrl\": null },",
            "\"Instances\": [],",
            $"\"Indexing\": {{ \"Roles\": [ \"{IndexerRole}\" ] }}",
        ]);

    /// <summary>
    /// <c>appsettings.Testing.json</c> gets the test key's hash and one app instance without a company, so the generated tests
    /// can read the manifest and extract.
    /// </summary>
    public static string EnsureConnectorTestingSettings(string json)
        => EnsureAiSubsection(json, "Connector",
        [
            "\"Enabled\": true,",
            $"\"ApiKeyHashes\": [ \"{TestApiKeyHash}\" ],",
            // A throwaway platform: the tests sign no envelope, and revocation signals go nowhere (.invalid never resolves).
            $"\"Platform\": {{ \"Issuer\": \"https://ai-platform.invalid\", \"SigningKeys\": {System.Text.Json.JsonSerializer.Serialize(TestSigningKeys)}, \"BaseUrl\": \"https://ai-platform.invalid\", \"ApiKey\": \"test-revocation-key\" }},",
            $"\"Instances\": [ {{ \"AppInstanceId\": \"{TestInstance}\", \"PlatformTenantId\": \"test-tenant\" }} ],",
            "\"Indexing\": { \"ChangesSettleDelay\": \"00:00:00\" }",
        ]);

    /// <summary>Adds <c>"Ai": { "{sub}": { ... } }</c>, or the subsection inside an existing <c>Ai</c> section.</summary>
    internal static string EnsureAiSubsection(string json, string sub, IReadOnlyList<string> bodyLines)
    {
        var nl = WebhooksWiring.NewLine(json);
        var ai = AiSection().Match(json);
        if (!ai.Success)
        {
            var lines = new List<string> { $"\"{sub}\": {{" };
            lines.AddRange(bodyLines.Select(l => "  " + l));
            lines.Add("}");
            return WebhooksWiring.EnsureTopLevelSection(json, "Ai", lines);
        }

        if (Regex.IsMatch(json[ai.Index..], $"\"{Regex.Escape(sub)}\"\\s*:"))
            return json;

        var open = ai.Index + ai.Length;
        var close = json.IndexOf('}', open);
        var isEmpty = close >= 0 && json[open..close].Trim().Length == 0;
        var body = string.Concat(bodyLines.Select(l => "      " + l + nl));
        var result = json[..open] + nl + $"    \"{sub}\": {{{nl}{body}    }}{(isEmpty ? string.Empty : ",")}" + json[open..];
        return IsJson(result) ? result : json;
    }

    private static bool IsJson(string json)
    {
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(json, new System.Text.Json.JsonDocumentOptions
            {
                CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    // ── generate-crud --ai ──────────────────────────────────────────

    /// <summary>The names <c>generate-crud --ai</c> gives an entity's AI surface.</summary>
    internal sealed record AiNames(string ResourceType, string ListCapability, string Permission)
    {
        public string SearchCapability => ResourceType + ".Search";
        public string CalculateCapability => ResourceType + ".Calculate";
    }

    public static AiNames NamesFor(string rootNamespace, string module, string entity, string permission)
    {
        var app = string.Concat(rootNamespace.Split('.').Select(p => Regex.Replace(p, "[^A-Za-z0-9]", string.Empty)));
        return new AiNames($"{module}.{entity}", $"{app}.{module}.{entity}.List", permission);
    }

    /// <summary>
    /// The entity gets <c>[AiIndexed]</c> (its records go to the platform's index) and <c>[AiQueryable]</c> over its scalar
    /// properties (generated <c>Search</c> and <c>Calculate</c>). An entity with no scalar property gets <c>[AiIndexed]</c> only.
    /// </summary>
    public static string MarkEntity(string source, string entity, AiNames names)
    {
        var declaration = ClassDeclaration(entity).Match(source);
        if (!declaration.Success)
            return source;

        var nl = WebhooksWiring.NewLine(source);
        var attributes = new List<string>();
        if (!source.Contains("[AiIndexed(", StringComparison.Ordinal))
            attributes.Add($"[AiIndexed(\"{names.ResourceType}\")]");
        var fields = ScalarFields(source);
        if (!source.Contains("[AiQueryable(", StringComparison.Ordinal) && fields.Count > 0)
        {
            var list = string.Join(", ", fields.Select(f => $"\"{f}\""));
            attributes.Add($"[AiQueryable(\"{names.ResourceType}\", \"{CodeGen.Pluralize(entity)} of the {names.ResourceType.Split('.')[0]} module.\", \"{names.Permission}\", Fields = [{list}])]");
        }

        if (attributes.Count == 0)
            return source;
        var text = source.Insert(declaration.Index, string.Concat(attributes.Select(a => a + nl)));
        return WebhooksWiring.EnsureUsing(text, "using Modulus.Core.Abstractions.Ai;");
    }

    /// <summary>The list query becomes a capability (and, on a host with permissions, checks the CRUD permission in the mediator).</summary>
    public static string MarkListQuery(string source, string queryType, string entityPlural, AiNames names, bool requirePermission)
        => MarkQuery(source, queryType, requirePermission ? names.Permission : null,
            $"[AiCapability(\"{names.ListCapability}\", \"Lists the {entityPlural.ToLowerInvariant()} of the {names.ResourceType.Split('.')[0]} module.\", ResourceType = \"{names.ResourceType}\")]",
            "[AiCapability(");

    /// <summary>The lookup query becomes the resource type's record source (citations, authorization checks, extraction).</summary>
    public static string MarkLookupQuery(
        string source, string queryType, string entity, string? titleField, AiNames names, bool requirePermission, string? batchQuery = null)
    {
        var title = titleField is null ? string.Empty : $", TitleField = \"{titleField}\"";
        var batch = batchQuery is null ? string.Empty : $", BatchLookup = typeof({batchQuery})";
        var marked = MarkQuery(source, queryType, requirePermission ? names.Permission : null,
            $"[AiResource(\"{names.ResourceType}\", \"One {entity.ToLowerInvariant()} of the {names.ResourceType.Split('.')[0]} module, by id.\"{title}{batch})]",
            "[AiResource(");
        return batchQuery is null ? marked : EnsureBatchLookup(marked, batchQuery);
    }

    /// <summary>An <c>[AiResource]</c> marked before batch lookups existed gets <c>BatchLookup = typeof(...)</c>.</summary>
    internal static string EnsureBatchLookup(string source, string batchQuery)
    {
        var attribute = System.Text.RegularExpressions.Regex.Match(source, @"\[AiResource\((?<args>[^\]]*)\)\]");
        if (!attribute.Success || attribute.Groups["args"].Value.Contains("BatchLookup", StringComparison.Ordinal))
            return source;

        var args = attribute.Groups["args"];
        return source.Insert(args.Index + args.Length, $", BatchLookup = typeof({batchQuery})");
    }

    /// <summary>Adds <c>GetByIdsAsync</c> to a repository interface or implementation that predates it; unchanged when it has it or has an unknown shape.</summary>
    internal static string EnsureRepositoryByIds(string source, string entity, bool implementation)
    {
        if (source.Contains("GetByIdsAsync", StringComparison.Ordinal))
            return source;

        var nl = WebhooksWiring.NewLine(source);
        var last = source.LastIndexOf('}');
        if (last < 0)
            return source;

        var member = implementation
            ? $"{nl}    public async Task<IReadOnlyList<{entity}>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct){nl}        => await _dbSet.AsNoTracking().Where(e => ids.Contains(e.Id)).ToListAsync(ct);{nl}"
            : $"{nl}    /// <summary>The {entity.ToLowerInvariant()}s with these ids, in one query.</summary>{nl}    Task<IReadOnlyList<{entity}>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);{nl}";
        // An implementation must have the _dbSet field the template declares, or the member would not compile.
        if (implementation && !source.Contains("_dbSet", StringComparison.Ordinal))
            return source;
        return source.Insert(last, member);
    }

    private static string MarkQuery(string source, string queryType, string? permission, string attribute, string marker)
    {
        var declaration = RecordDeclaration(queryType).Match(source);
        if (!declaration.Success)
            return source;

        var nl = WebhooksWiring.NewLine(source);
        var attributes = new List<string>();
        if (!source.Contains(marker, StringComparison.Ordinal))
            attributes.Add(attribute);
        if (permission is not null && !source.Contains("[RequirePermission(", StringComparison.Ordinal))
            attributes.Add($"[RequirePermission(\"{permission}\")]");
        if (attributes.Count == 0)
            return source;

        var text = source.Insert(declaration.Index, string.Concat(attributes.Select(a => a + nl)));
        text = WebhooksWiring.EnsureUsing(text, "using Modulus.Core.Abstractions.Ai;");
        return permission is null ? text : WebhooksWiring.EnsureUsing(text, "using Modulus.Mediator.Abstractions.Attributes;");
    }

    /// <summary>Public scalar properties with a getter (filterable by <c>[AiQueryable]</c>), except the key and the company.</summary>
    internal static List<string> ScalarFields(string source) =>
        ScalarProperty().Matches(source)
            .Select(m => m.Groups["name"].Value)
            .Where(n => n is not ("Id" or "TenantId"))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>The first string property, the natural title of a record (<c>TitleField</c>).</summary>
    internal static string? TitleField(string entitySource) =>
        ScalarProperty().Matches(entitySource)
            .Where(m => m.Groups["type"].Value == "string")
            .Select(m => m.Groups["name"].Value)
            .FirstOrDefault(n => n is not ("Id" or "TenantId"));

    private static Regex ClassDeclaration(string type) =>
        new($@"^[ \t]*public\s+(?:sealed\s+|abstract\s+|partial\s+)*class\s+{Regex.Escape(type)}\b", RegexOptions.Multiline);

    private static Regex RecordDeclaration(string type) =>
        new($@"^[ \t]*public\s+(?:sealed\s+|partial\s+)*(?:record|class)\s+{Regex.Escape(type)}\b", RegexOptions.Multiline);

    [GeneratedRegex(@"^\s*public\s+(?<type>string|bool|int|long|short|decimal|double|float|DateTime|DateTimeOffset|DateOnly|TimeOnly|Guid)\??\s+(?<name>[A-Z][A-Za-z0-9_]*)\s*\{\s*get;", RegexOptions.Multiline)]
    private static partial Regex ScalarProperty();

    [GeneratedRegex("^\\s{2}\"Ai\"\\s*:\\s*\\{", RegexOptions.Multiline)]
    private static partial Regex AiSection();
}
