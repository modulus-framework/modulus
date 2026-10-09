namespace Modulus.AI.Connector.Testing;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Modulus.AI.Connector.Contract;
using Modulus.Core.Abstractions;

/// <summary>
/// The connector conformance suite, run inside the app's own tests against a fake platform (Architecture §9.3): the same
/// categories the platform certifies before it activates a connector, checked over HTTP against the running host.
/// </summary>
/// <example>
/// <code>
/// using var platform = new AiFakePlatform();
/// await using var factory = new ModulusWebAppFactory&lt;Program&gt;()
///     .WithWebHostBuilder(b =&gt; b.ConfigureTestServices(platform.Configure));
/// var report = await AiConnectorConformance.RunAsync(factory.Services, factory.CreateClient(), platform,
///     new AiConformanceOptions { User = userId.ToString() });
/// report.EnsurePassed();
/// </code>
/// </example>
public static class AiConnectorConformance
{
    /// <summary>The wire contract major version this suite checks.</summary>
    public const string ContractVersion = "1";

    /// <summary>
    /// Runs every check against the host behind <paramref name="client"/>, whose connector <paramref name="platform"/>
    /// configured (<see cref="AiFakePlatform.Configure"/>). Checks that need a user run as
    /// <see cref="AiConformanceOptions.User"/>; the access change of <see cref="AiConformanceOptions.ChangeUserAccess"/>
    /// runs last.
    /// </summary>
    public static async Task<AiConformanceReport> RunAsync(
        IServiceProvider services,
        HttpClient client,
        AiFakePlatform platform,
        AiConformanceOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(platform);
        var run = new ConformanceRun(services, client, platform, options ?? new AiConformanceOptions(), ct);
        return new AiConformanceReport(await run.ExecuteAsync().ConfigureAwait(false));
    }
}

/// <summary>One conformance run: the checks in order, sharing what earlier ones learned (manifest, scope, visible records).</summary>
internal sealed class ConformanceRun(
    IServiceProvider services,
    HttpClient client,
    AiFakePlatform platform,
    AiConformanceOptions options,
    CancellationToken ct)
{
    private const string UnmappedInstance = "conformance-unmapped-instance";
    private const string UnknownResourceType = "Conformance.Unknown";
    private const string UndeclaredName = "conformanceUndeclared";

    // Quotes, brackets, DAX, statement separators and comments (FR-26a): values only, never query text.
    private static readonly string[] InjectionValues =
    [
        "' OR '1'='1",
        "\"; DROP TABLE users; --",
        "x') ] } EVALUATE ROW(\"leak\", 1)",
        "/* comment */ x -- ",
        "%_[]^\\",
    ];

    private static readonly HashSet<string> KnownCodes =
    [
        ConnectorErrorCodes.Denied, ConnectorErrorCodes.NotFound, ConnectorErrorCodes.Unavailable,
        ConnectorErrorCodes.RateLimited, ConnectorErrorCodes.InvalidRequest,
    ];

    private readonly ModulusAiConnectorOptions _connector = services.GetRequiredService<IOptions<ModulusAiConnectorOptions>>().Value;
    private readonly List<AiConformanceResult> _results = [];
    private readonly List<string> _untypedErrors = [];
    private readonly List<ResourceReference> _visible = [];
    private readonly List<ResourceReference> _readable = [];
    private ConnectorHealth? _health;
    private ConnectorManifest? _manifest;
    private AccessScope? _scope;

    private ConnectorManifest Manifest => _manifest ?? throw new NotApplicableException("the manifest could not be read");

    private AccessScope Scope => _scope ?? throw new NotApplicableException(options.User is null
        ? "no conformance user (AiConformanceOptions.User)"
        : "the conformance user could not call the connector");

    private string User => _scope is not null ? options.User! : throw new NotApplicableException(options.User is null
        ? "no conformance user (AiConformanceOptions.User)"
        : "the conformance user could not call the connector");

    // Without a user the deny probes still run, as an account the app does not know: they then also pass for an app that
    // refuses everyone, so the user makes them meaningful.
    private string Someone => _scope is not null ? options.User! : Guid.NewGuid().ToString("D");

    public async Task<IReadOnlyList<AiConformanceResult>> ExecuteAsync()
    {
        await HealthAsync().ConfigureAwait(false);
        await ManifestAsync().ConfigureAwait(false);
        await AuthenticationAsync().ConfigureAwait(false);
        await TenantIsolationAsync().ConfigureAwait(false);
        await DenyPathsAsync().ConfigureAwait(false);
        await FieldSecurityAsync().ConfigureAwait(false);
        await QueryInjectionAsync().ConfigureAwait(false);
        await AuthorizationAsync().ConfigureAwait(false);
        await ExtractionAsync().ConfigureAwait(false);
        await RevocationAsync().ConfigureAwait(false);
        await TimeoutIsDenyAsync().ConfigureAwait(false);
        await NoAdapterCachingAsync().ConfigureAwait(false);

        Check(AiConformanceCategory.DenyPaths, "every refusal is a typed error", () =>
            Require(_untypedErrors.Count == 0, string.Join("; ", _untypedErrors.Distinct(StringComparer.Ordinal).Take(5))));
        return _results;
    }

    // ── Health ──────────────────────────────────────────────────────

    private Task HealthAsync()
        => CheckAsync(AiConformanceCategory.Health, "health answers ok with the contract version", async () =>
        {
            var answer = await SendAsync(HttpMethod.Get, "/health").ConfigureAwait(false);
            ExpectOk(answer);
            _health = answer.Read<ConnectorHealth>();
            Require(_health.Status == "ok", $"status is '{_health.Status}'");
            Require(_health.ContractVersion == AiConnectorConformance.ContractVersion,
                $"contract version '{_health.ContractVersion}', expected '{AiConnectorConformance.ContractVersion}'");
        });

    // ── Manifest ────────────────────────────────────────────────────

    private async Task ManifestAsync()
    {
        string? first = null;
        await CheckAsync(AiConformanceCategory.Manifest, "the manifest is served to the platform's API key", async () =>
        {
            var answer = await SendAsync(HttpMethod.Get, "/manifest").ConfigureAwait(false);
            ExpectOk(answer);
            _manifest = answer.Read<ConnectorManifest>();
            first = answer.Body;
        }).ConfigureAwait(false);

        Check(AiConformanceCategory.Manifest, "the manifest names the contract version, the app and a fingerprint", () =>
        {
            Require(Manifest.ContractVersion == AiConnectorConformance.ContractVersion, $"contract version '{Manifest.ContractVersion}'");
            Require(_health is null || _health.ContractVersion == Manifest.ContractVersion, "health and manifest disagree on the contract version");
            Require(!string.IsNullOrWhiteSpace(Manifest.AppType), "no appType");
            Require(!string.IsNullOrWhiteSpace(Manifest.Fingerprint), "no fingerprint");
        });

        await CheckAsync(AiConformanceCategory.Manifest, "the manifest is stable between calls", async () =>
        {
            _ = Manifest;
            var again = await SendAsync(HttpMethod.Get, "/manifest").ConfigureAwait(false);
            ExpectOk(again);
            Require(again.Read<ConnectorManifest>().Fingerprint == Manifest.Fingerprint, "the fingerprint changed");
            Require(again.Body == first, "the manifest body changed");
        }).ConfigureAwait(false);

        Check(AiConformanceCategory.Manifest, "every capability is read-only, named once and takes an object", () =>
        {
            foreach (var capability in Manifest.Capabilities)
            {
                Require(!string.IsNullOrWhiteSpace(capability.Name), "a capability has no name");
                Require(capability.ReadOnly, $"{capability.Name} is not read-only");
                Require(!string.IsNullOrWhiteSpace(capability.Description), $"{capability.Name} has no description");
                Require(capability.Description.Length <= _connector.MaxDescriptionLength, $"{capability.Name}'s description is too long");
                Require(capability.InputSchema is JsonObject schema && (string?)schema["type"] == "object",
                    $"{capability.Name}'s input schema is not an object schema");
            }

            var twice = Manifest.Capabilities.GroupBy(c => c.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            Require(twice is null, $"capability {twice?.Key} is declared twice");
        });

        Check(AiConformanceCategory.Manifest, "every capability's resource type is declared", () =>
        {
            var types = Manifest.ResourceTypes.Select(r => r.Type).ToHashSet(StringComparer.Ordinal);
            var orphan = Manifest.Capabilities.FirstOrDefault(c => c.ResourceType is not null && !types.Contains(c.ResourceType));
            Require(orphan is null, $"{orphan?.Name} returns undeclared resource type {orphan?.ResourceType}");
        });

        Check(AiConformanceCategory.Manifest, "resource types are named once and their title fields exist", () =>
        {
            var twice = Manifest.ResourceTypes.GroupBy(r => r.Type, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            Require(twice is null, $"resource type {twice?.Key} is declared twice");
            foreach (var type in Manifest.ResourceTypes)
            {
                Require(!string.IsNullOrWhiteSpace(type.Description), $"{type.Type} has no description");
                Require(type.TitleField is null || type.Fields.Any(f => f.Name == type.TitleField),
                    $"{type.Type}'s title field {type.TitleField} is not one of its fields");
            }
        });

        Check(AiConformanceCategory.Manifest, "every field is typed, classified and named once", () =>
        {
            var lists = Manifest.Capabilities.Select(c => (c.Name, c.OutputFields))
                .Concat(Manifest.ResourceTypes.Select(r => (Name: r.Type, OutputFields: r.Fields)));
            foreach (var (owner, fields) in lists)
            {
                foreach (var field in fields)
                {
                    Require(!string.IsNullOrWhiteSpace(field.Name) && !string.IsNullOrWhiteSpace(field.Type), $"{owner} has an unnamed or untyped field");
                    Require(Enum.IsDefined(field.Classification), $"{owner}.{field.Name} has no platform classification");
                }

                var twice = fields.GroupBy(f => f.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
                Require(twice is null, $"{owner} declares field {twice?.Key} twice");
            }
        });
    }

    // ── Authentication ──────────────────────────────────────────────

    private async Task AuthenticationAsync()
    {
        // First, so the deny probes below differ from an accepted call only in what they break.
        await CheckAsync(AiConformanceCategory.Authentication, "a valid envelope for a known user is accepted", async () =>
        {
            if (options.User is null)
                throw new NotApplicableException("no conformance user (AiConformanceOptions.User)");
            var answer = await SendAsync(HttpMethod.Post, "/authz/scope", Envelope(options.User), "{}").ConfigureAwait(false);
            Require(answer.Status == 200,
                $"the conformance user got {answer.Describe()}: is the account active, resolved by the connector's user claim, and a member of the instance's company?");
            _scope = answer.Read<AccessScope>();
        }).ConfigureAwait(false);

        await DeniedAsync("a call without an API key is denied", HttpMethod.Get, "/manifest", apiKey: string.Empty).ConfigureAwait(false);
        await DeniedAsync("a user call without an API key is denied", HttpMethod.Post, "/authz/scope", Envelope(Someone), apiKey: string.Empty).ConfigureAwait(false);
        await DeniedAsync("an unknown API key is denied", HttpMethod.Post, "/authz/scope", Envelope(Someone), apiKey: "conformance-wrong-key").ConfigureAwait(false);
        await CheckAsync(AiConformanceCategory.Authentication, "a call from a browser is denied", async () =>
        {
            using var request = Request(HttpMethod.Post, "/authz/scope", Envelope(Someone), "{}");
            request.Headers.Add("Origin", "https://conformance.invalid");
            ExpectError(await SendAsync(request).ConfigureAwait(false), 401, ConnectorErrorCodes.Denied);
        }).ConfigureAwait(false);
        await DeniedAsync("a user call without an envelope is denied", HttpMethod.Post, "/authz/scope").ConfigureAwait(false);
        await DeniedAsync("an envelope signed by an unknown key is denied", HttpMethod.Post, "/authz/scope",
            platform.Envelope(Someone, userClaim: _connector.Users.Claim, signedByStranger: true)).ConfigureAwait(false);
        await DeniedAsync("an unsigned envelope is denied", HttpMethod.Post, "/authz/scope", platform.UnsignedEnvelope(Someone)).ConfigureAwait(false);
        await DeniedAsync("an expired envelope is denied", HttpMethod.Post, "/authz/scope", Envelope(Someone, d =>
        {
            var past = DateTime.UtcNow - _connector.Platform.ClockSkew - TimeSpan.FromMinutes(2);
            d.IssuedAt = past.AddSeconds(-60);
            d.NotBefore = d.IssuedAt;
            d.Expires = past;
        })).ConfigureAwait(false);
        await DeniedAsync("an envelope from another issuer is denied", HttpMethod.Post, "/authz/scope",
            Envelope(Someone, d => d.Issuer = "https://impostor.conformance.invalid")).ConfigureAwait(false);
        await DeniedAsync("an envelope valid for too long is denied", HttpMethod.Post, "/authz/scope",
            Envelope(Someone, d => d.Expires = DateTime.UtcNow + _connector.Platform.MaxEnvelopeLifetime + TimeSpan.FromMinutes(5))).ConfigureAwait(false);
        await DeniedAsync("an envelope without an id is denied", HttpMethod.Post, "/authz/scope",
            Envelope(Someone, d => d.Claims.Remove("jti"))).ConfigureAwait(false);
        await DeniedAsync("an envelope for an unknown user is denied", HttpMethod.Post, "/authz/scope",
            Envelope("conformance-unknown-" + Guid.NewGuid().ToString("N"))).ConfigureAwait(false);

        await CheckAsync(AiConformanceCategory.Authentication, "a replayed envelope is denied", async () =>
        {
            var envelope = Envelope(User);
            ExpectOk(await SendAsync(HttpMethod.Post, "/authz/scope", envelope, "{}").ConfigureAwait(false));
            ExpectError(await SendAsync(HttpMethod.Post, "/authz/scope", envelope, "{}").ConfigureAwait(false), 401, ConnectorErrorCodes.Denied);
        }).ConfigureAwait(false);
    }

    private Task DeniedAsync(string check, HttpMethod method, string path, string? envelope = null, string? apiKey = null)
        => CheckAsync(AiConformanceCategory.Authentication, check, async () =>
            ExpectError(await SendAsync(method, path, envelope, method == HttpMethod.Post ? "{}" : null, apiKey).ConfigureAwait(false),
                401, ConnectorErrorCodes.Denied));

    // ── Tenant isolation ────────────────────────────────────────────

    private async Task TenantIsolationAsync()
    {
        const AiConformanceCategory category = AiConformanceCategory.TenantIsolation;
        await CheckAsync(category, "an envelope for an app instance this app does not serve is denied", async () =>
            ExpectError(await SendAsync(HttpMethod.Post, "/authz/scope", platform.Envelope(Someone, UnmappedInstance, userClaim: _connector.Users.Claim), "{}")
                .ConfigureAwait(false), 401, ConnectorErrorCodes.Denied)).ConfigureAwait(false);
        await CheckAsync(category, "an envelope pairing an app instance with another platform tenant is denied", async () =>
            ExpectError(await SendAsync(HttpMethod.Post, "/authz/scope", platform.Envelope(Someone, platformTenantId: platform.OtherPlatformTenantId, userClaim: _connector.Users.Claim), "{}")
                .ConfigureAwait(false), 401, ConnectorErrorCodes.Denied)).ConfigureAwait(false);
        await CheckAsync(category, "an envelope addressed to another app instance is denied", async () =>
            ExpectError(await SendAsync(HttpMethod.Post, "/authz/scope", Envelope(Someone, d => d.Audience = platform.OtherAppInstanceId), "{}")
                .ConfigureAwait(false), 401, ConnectorErrorCodes.Denied)).ConfigureAwait(false);
        await CheckAsync(category, "extraction for an app instance this app does not serve is denied", async () =>
        {
            ExpectError(await SendAsync(HttpMethod.Get, $"/extract?appInstanceId={UnmappedInstance}").ConfigureAwait(false), 403, ConnectorErrorCodes.Denied);
            ExpectError(await SendAsync(HttpMethod.Get, $"/changes?appInstanceId={UnmappedInstance}").ConfigureAwait(false), 403, ConnectorErrorCodes.Denied);
            ExpectError(await SendAsync(HttpMethod.Get, "/extract").ConfigureAwait(false), 403, ConnectorErrorCodes.Denied);
        }).ConfigureAwait(false);

        Check(category, "the scope is the envelope's app instance and company, for at most five minutes", () =>
        {
            Require(Scope.AppInstanceId == platform.AppInstanceId, $"scope of instance {Scope.AppInstanceId}");
            Require(!string.IsNullOrWhiteSpace(Scope.RevocationKey), "no revocation key");
            Require(Scope.TtlSeconds is > 0 and <= 300, $"ttl {Scope.TtlSeconds} s");
            var companies = Scope.DataScopes.TryGetValue("company", out var c) ? c : [];
            Require(platform.CompanyId is { } company
                    ? companies.SequenceEqual([company.ToString("D")])
                    : companies.Length == 0,
                $"company scope [{string.Join(", ", companies)}]");
        });

        await CheckAsync(category, "another app instance gets its own scope, never this company's", async () =>
        {
            var answer = await SendAsync(HttpMethod.Post, "/authz/scope",
                platform.Envelope(User, platform.OtherAppInstanceId, platform.OtherPlatformTenantId, _connector.Users.Claim), "{}").ConfigureAwait(false);
            if (answer.Status == 401)
                return; // The other instance does not accept the user at all: isolated too.
            ExpectOk(answer);
            var other = answer.Read<AccessScope>();
            Require(other.AppInstanceId == platform.OtherAppInstanceId, $"scope of instance {other.AppInstanceId}");
            Require(other.RevocationKey != Scope.RevocationKey, "both instances share a revocation key");
            Require(platform.CompanyId is not { } company
                    || !other.DataScopes.TryGetValue("company", out var companies)
                    || !companies.Contains(company.ToString("D")),
                "the other instance's scope includes this instance's company");
        }).ConfigureAwait(false);
    }

    // ── Deny paths ──────────────────────────────────────────────────

    private async Task DenyPathsAsync()
    {
        const AiConformanceCategory category = AiConformanceCategory.DenyPaths;
        await CheckAsync(category, "an unknown capability answers NOT_FOUND", async () =>
            ExpectError(await Execute("Conformance.Unknown.Capability", "{}").ConfigureAwait(false), 404, ConnectorErrorCodes.NotFound)).ConfigureAwait(false);
        await CheckAsync(category, "a malformed body answers INVALID_REQUEST", async () =>
        {
            var name = Usable().FirstOrDefault()?.Name ?? "Conformance.Unknown.Capability";
            ExpectError(await SendAsync(HttpMethod.Post, $"/capabilities/{Uri.EscapeDataString(name)}:execute", Envelope(User), "{not json").ConfigureAwait(false),
                400, ConnectorErrorCodes.InvalidRequest);
            ExpectError(await SendAsync(HttpMethod.Post, "/authz/resources:check", Envelope(User), "{not json").ConfigureAwait(false),
                400, ConnectorErrorCodes.InvalidRequest);
            ExpectError(await SendAsync(HttpMethod.Post, "/resources:get", Envelope(User), "{}").ConfigureAwait(false),
                400, ConnectorErrorCodes.InvalidRequest);
        }).ConfigureAwait(false);
        await CheckAsync(category, "arguments that are not an object answer INVALID_REQUEST", async () =>
        {
            var capability = Usable().FirstOrDefault() ?? throw new NotApplicableException("no capability the user may call");
            ExpectError(await Execute(capability.Name, """{"args":[1,2]}""").ConfigureAwait(false), 400, ConnectorErrorCodes.InvalidRequest);
        }).ConfigureAwait(false);
        await CheckAsync(category, "an unknown resource type answers NOT_FOUND", async () =>
            ExpectError(await GetResource(UnknownResourceType, "1").ConfigureAwait(false), 404, ConnectorErrorCodes.NotFound)).ConfigureAwait(false);
        await CheckAsync(category, "a record that does not exist is never returned", async () =>
        {
            _ = User;
            if (Manifest.ResourceTypes.Count == 0)
                throw new NotApplicableException("no resource type");
            foreach (var type in Manifest.ResourceTypes)
            {
                var answer = await GetResource(type.Type, Guid.NewGuid().ToString("D")).ConfigureAwait(false);
                Require(answer.Status == 404 && answer.Error?.Code == ConnectorErrorCodes.NotFound
                        || answer.Status == 403 && answer.Error?.Code == ConnectorErrorCodes.Denied,
                    $"{type.Type}: {answer.Describe()}");
            }
        }).ConfigureAwait(false);
    }

    // ── Field security ──────────────────────────────────────────────

    private async Task FieldSecurityAsync()
    {
        const AiConformanceCategory category = AiConformanceCategory.FieldSecurity;
        await CheckAsync(category, "capability results hold only declared fields the user may read", async () =>
        {
            var ran = 0;
            foreach (var capability in Usable())
            {
                if (MinimalArguments(capability) is not { } args)
                    continue;
                var answer = await Execute(capability.Name, args).ConfigureAwait(false);
                if (answer.Status is 400 or 403 or 404)
                    continue;
                ExpectOk(answer, capability.Name);
                ran++;

                var declared = capability.OutputFields.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
                var prefix = capability.ResourceType ?? capability.Name;
                foreach (var record in answer.Read<CapabilityResult>().Resources)
                {
                    RequireFields(capability.Name, record.Fields.Keys, declared, prefix);
                    if (record.Reference is { } reference)
                    {
                        Require(capability.ResourceType is null || reference.ResourceType == capability.ResourceType,
                            $"{capability.Name} returned a {reference.ResourceType}");
                        if (!_visible.Any(v => v.ResourceType == reference.ResourceType && v.ResourceId == reference.ResourceId))
                            _visible.Add(reference);
                    }
                }
            }

            if (ran == 0)
                throw new NotApplicableException("no capability answered the user's minimal arguments");
        }).ConfigureAwait(false);

        await CheckAsync(category, "records hold only declared fields the user may read", async () =>
        {
            if (_visible.Count == 0)
                throw new NotApplicableException("no capability returned a record");
            foreach (var reference in _visible.Take(10))
            {
                var answer = await GetResource(reference.ResourceType, reference.ResourceId).ConfigureAwait(false);
                if (answer.Status == 403 && answer.Error?.Code == ConnectorErrorCodes.Denied)
                    continue; // The lookup is stricter than the capability that listed it.
                ExpectOk(answer, $"{reference.ResourceType} {reference.ResourceId}");
                var type = Manifest.ResourceTypes.First(t => t.Type == reference.ResourceType);
                var record = answer.Read<AppResource>();
                Require(record.Reference is null || record.Reference.ResourceId == reference.ResourceId, "the record is another one");
                RequireFields(type.Type, record.Fields.Keys, type.Fields.Select(f => f.Name).ToHashSet(StringComparer.Ordinal), type.Type);
                _readable.Add(reference);
            }
        }).ConfigureAwait(false);

        await CheckAsync(category, "field checks return only requested fields and agree with the scope", async () =>
        {
            _ = User;
            if (Manifest.ResourceTypes.Count == 0)
                throw new NotApplicableException("no resource type");
            foreach (var type in Manifest.ResourceTypes)
            {
                var requested = type.Fields.Select(f => f.Name).Append(UndeclaredName).ToList();
                var body = JsonSerializer.Serialize(
                    new FieldsCheckRequest(new ResourceReference(type.Type, Guid.NewGuid().ToString("D")), requested), ConnectorJson.Options);
                var answer = await SendAsync(HttpMethod.Post, "/authz/fields:check", Envelope(User), body).ConfigureAwait(false);
                ExpectOk(answer, type.Type);
                var allowed = answer.Read<FieldsCheckResult>().AllowedFields;
                Require(allowed.All(requested.Contains), $"{type.Type}: allowed fields that were not asked for");
                Require(!allowed.Contains(UndeclaredName), $"{type.Type}: an undeclared field was allowed");
                foreach (var field in type.Fields)
                {
                    if (Scope.FieldPolicies.TryGetValue($"{type.Type}.{field.Name}", out var policy))
                    {
                        Require(allowed.Contains(field.Name) == (policy == FieldAccessPolicy.Allow),
                            $"{type.Type}.{field.Name}: the scope says {policy}, the field check disagrees");
                    }
                }
            }
        }).ConfigureAwait(false);
    }

    private void RequireFields(string owner, IEnumerable<string> fields, HashSet<string> declared, string policyPrefix)
    {
        foreach (var field in fields)
        {
            Require(declared.Contains(field), $"{owner} returned undeclared field '{field}'");
            Require(!Scope.FieldPolicies.TryGetValue($"{policyPrefix}.{field}", out var policy) || policy == FieldAccessPolicy.Allow,
                $"{owner} returned '{field}', which the user's scope denies");
        }
    }

    // ── Query injection ─────────────────────────────────────────────

    private async Task QueryInjectionAsync()
    {
        const AiConformanceCategory category = AiConformanceCategory.QueryInjection;
        await CheckAsync(category, "undeclared argument names are refused", async () =>
        {
            var usable = Usable().ToList();
            if (usable.Count == 0)
                throw new NotApplicableException("no capability the user may call");
            foreach (var capability in usable)
            {
                var args = Arguments(capability, new JsonObject { [UndeclaredName] = "x" });
                ExpectError(await Execute(capability.Name, args).ConfigureAwait(false), 400, ConnectorErrorCodes.InvalidRequest, capability.Name);
                if (FilterFields(capability).Count > 0)
                {
                    var filter = Arguments(capability, new JsonObject
                    {
                        ["filters"] = new JsonArray(new JsonObject { ["field"] = UndeclaredName, ["op"] = "eq", ["value"] = "x" }),
                    });
                    ExpectError(await Execute(capability.Name, filter).ConfigureAwait(false), 400, ConnectorErrorCodes.InvalidRequest, $"{capability.Name} filter");
                }
            }
        }).ConfigureAwait(false);

        await CheckAsync(category, "query syntax in argument values never breaks a call", async () =>
        {
            var probes = 0;
            foreach (var capability in Usable())
            {
                foreach (var property in StringProperties(capability))
                {
                    foreach (var value in InjectionValues)
                    {
                        probes++;
                        RequireHandled(await Execute(capability.Name, Arguments(capability, new JsonObject { [property] = value })).ConfigureAwait(false),
                            $"{capability.Name} {property}={value}");
                    }
                }

                foreach (var field in FilterFields(capability).Take(5))
                {
                    foreach (var value in InjectionValues)
                    {
                        probes++;
                        var args = Arguments(capability, new JsonObject
                        {
                            ["filters"] = new JsonArray(new JsonObject { ["field"] = field, ["op"] = "eq", ["value"] = value }),
                        });
                        RequireHandled(await Execute(capability.Name, args).ConfigureAwait(false), $"{capability.Name} filter {field}={value}");
                    }
                }
            }

            if (probes == 0)
                throw new NotApplicableException("no capability the user may call takes text");
        }).ConfigureAwait(false);
    }

    private static void RequireHandled(Answer answer, string probe)
        => Require(answer.Status is 200 or 400 or 403 or 404, $"{probe}: {answer.Describe()}");

    // ── Authorization (batch) ───────────────────────────────────────

    private async Task AuthorizationAsync()
    {
        const AiConformanceCategory category = AiConformanceCategory.Authorization;
        await CheckAsync(category, "batch checks answer each record, in order, with partial denies", async () =>
        {
            _ = User;
            var references = new List<(ResourceReference Reference, bool? Expected)>();
            if (_readable.FirstOrDefault() is { } readable)
                references.Add((readable, true));
            foreach (var type in Manifest.ResourceTypes)
                references.Add((new ResourceReference(type.Type, Guid.NewGuid().ToString("D")), false));
            references.Insert(Math.Min(1, references.Count), (new ResourceReference(UnknownResourceType, "1"), false));

            var body = JsonSerializer.Serialize(new ResourcesCheckRequest([.. references.Select(r => r.Reference)]), ConnectorJson.Options);
            var answer = await SendAsync(HttpMethod.Post, "/authz/resources:check", Envelope(User), body).ConfigureAwait(false);
            ExpectOk(answer);
            var decisions = answer.Read<ResourcesCheckResult>().Decisions;
            Require(decisions.Count == references.Count, $"{decisions.Count} decisions for {references.Count} records");
            for (var i = 0; i < decisions.Count; i++)
            {
                var (reference, expected) = references[i];
                Require(decisions[i].Reference.ResourceType == reference.ResourceType && decisions[i].Reference.ResourceId == reference.ResourceId,
                    $"decision {i} is for {decisions[i].Reference.ResourceType} {decisions[i].Reference.ResourceId}");
                Require(expected is null || decisions[i].Allowed == expected,
                    $"{reference.ResourceType} {reference.ResourceId}: allowed {decisions[i].Allowed}");
            }
        }).ConfigureAwait(false);

        await CheckAsync(category, "an empty batch answers no decisions", async () =>
        {
            var answer = await SendAsync(HttpMethod.Post, "/authz/resources:check", Envelope(User), """{"resources":[]}""").ConfigureAwait(false);
            ExpectOk(answer);
            Require(answer.Read<ResourcesCheckResult>().Decisions.Count == 0, "decisions for no records");
        }).ConfigureAwait(false);

        await CheckAsync(category, "an oversized batch is refused", async () =>
        {
            var references = Enumerable.Range(0, _connector.MaxBatchSize + 1).Select(i => new ResourceReference(UnknownResourceType, i.ToString(CultureInfo.InvariantCulture)));
            var body = JsonSerializer.Serialize(new ResourcesCheckRequest([.. references]), ConnectorJson.Options);
            ExpectError(await SendAsync(HttpMethod.Post, "/authz/resources:check", Envelope(User), body).ConfigureAwait(false), 400, ConnectorErrorCodes.InvalidRequest);
        }).ConfigureAwait(false);
    }

    // ── Extraction and change tracking ──────────────────────────────

    private async Task ExtractionAsync()
    {
        const AiConformanceCategory category = AiConformanceCategory.Extraction;
        var indexed = _manifest?.ResourceTypes.Where(r => r.Indexed).ToDictionary(r => r.Type, StringComparer.Ordinal);
        if (indexed is not { Count: > 0 })
        {
            _results.Add(new AiConformanceResult(category, "indexed resource types extract and track changes", AiConformanceOutcome.NotApplicable,
                indexed is null ? "the manifest could not be read" : "no resource type is indexed"));
            return;
        }

        var instance = Uri.EscapeDataString(platform.AppInstanceId);
        await CheckAsync(category, "extraction pages resume from their cursor without repeats", async () =>
        {
            var seen = new HashSet<(string, string)>();
            string? cursor = null;
            var pages = 0;
            do
            {
                var path = $"/extract?appInstanceId={instance}&limit={options.ExtractPageSize}" + (cursor is null ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor));
                var answer = await SendAsync(HttpMethod.Get, path).ConfigureAwait(false);
                Require(answer.Status != 403, $"the indexing identity may not read an indexed type ({answer.Describe()}): grant Ai:Connector:Indexing:Roles the read permissions");
                Require(answer.Status != 404, $"extraction is not offered ({answer.Describe()}): register an entity source (UseEntityFrameworkCore)");
                ExpectOk(answer, "extract");
                var page = answer.Read<ExtractPage>();
                foreach (var resource in page.Resources)
                {
                    var reference = resource.Reference;
                    Require(indexed.TryGetValue(reference.ResourceType, out var type), $"extracted an unindexed {reference.ResourceType}");
                    Require(seen.Add((reference.ResourceType, reference.ResourceId)), $"{reference.ResourceType} {reference.ResourceId} extracted twice");
                    Require(resource.Access is not null, $"{reference.ResourceType} {reference.ResourceId} carries no access metadata");
                    var declared = type!.Fields.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
                    foreach (var field in resource.Fields.Keys)
                        Require(declared.Contains(field), $"{reference.ResourceType} carries undeclared field '{field}'");
                }

                Require(page.NextCursor != cursor || page.NextCursor is null, "the cursor did not move");
                cursor = page.NextCursor;
                pages++;
            }
            while (cursor is not null && pages < options.MaxExtractPages);
        }).ConfigureAwait(false);

        await CheckAsync(category, "an invalid extraction cursor or type is refused", async () =>
        {
            ExpectError(await SendAsync(HttpMethod.Get, $"/extract?appInstanceId={instance}&cursor=conformance-invalid").ConfigureAwait(false), 400, ConnectorErrorCodes.InvalidRequest);
            ExpectError(await SendAsync(HttpMethod.Get, $"/extract?appInstanceId={instance}&resourceType={UnknownResourceType}").ConfigureAwait(false), 404, ConnectorErrorCodes.NotFound);
            ExpectError(await SendAsync(HttpMethod.Get, $"/extract?appInstanceId={instance}&limit=0").ConfigureAwait(false), 400, ConnectorErrorCodes.InvalidRequest);
        }).ConfigureAwait(false);

        await CheckAsync(category, "the change feed resumes from its cursor and tombstones are well-formed", async () =>
        {
            var seen = new HashSet<(string, string, DateTimeOffset)>();
            string? since = null;
            for (var pages = 0; pages < options.MaxExtractPages; pages++)
            {
                var path = $"/changes?appInstanceId={instance}&limit={options.ExtractPageSize}" + (since is null ? string.Empty : "&since=" + Uri.EscapeDataString(since));
                var answer = await SendAsync(HttpMethod.Get, path).ConfigureAwait(false);
                Require(answer.Status != 404, $"no change feed ({answer.Describe()}): register one (UseEntityFrameworkCore)");
                ExpectOk(answer, "changes");
                var page = answer.Read<ChangesPage>();
                Require(!string.IsNullOrWhiteSpace(page.Cursor), "no cursor");
                foreach (var change in page.Changes)
                {
                    var reference = change.Reference;
                    Require(indexed.ContainsKey(reference.ResourceType), $"a change of unindexed {reference.ResourceType}");
                    Require(seen.Add((reference.ResourceType, reference.ResourceId, change.OccurredAt)),
                        $"{reference.ResourceType} {reference.ResourceId} returned again after its cursor");
                    Require(change.Kind == ResourceChangeKind.Tombstone
                            ? change.Resource is null
                            : change.Resource is { } upsert && upsert.Reference.ResourceId == reference.ResourceId && upsert.Access is not null,
                        $"{change.Kind} of {reference.ResourceType} {reference.ResourceId} is malformed");
                }

                var last = since;
                since = page.Cursor;
                if (!page.HasMore && last is not null)
                    break;
            }
        }).ConfigureAwait(false);

        await CheckAsync(category, "an invalid change cursor is refused", async () =>
            ExpectError(await SendAsync(HttpMethod.Get, $"/changes?appInstanceId={instance}&since=conformance-invalid").ConfigureAwait(false), 400, ConnectorErrorCodes.InvalidRequest))
            .ConfigureAwait(false);
    }

    // ── Revocation ──────────────────────────────────────────────────

    private async Task RevocationAsync()
    {
        const AiConformanceCategory category = AiConformanceCategory.Revocation;
        var start = platform.Calls.Count;
        List<AiPlatformCall> calls = [];
        var acknowledged = false;

        await CheckAsync(category, "an access change is signalled to the platform's /revocations/scope", async () =>
        {
            platform.FailNext(2);
            try
            {
                if (options.TriggerAccessChange is { } trigger)
                {
                    await trigger(services, ct).ConfigureAwait(false);
                }
                else
                {
                    await using var scope = services.CreateAsyncScope();
                    await scope.ServiceProvider.GetServices<IAccessChangeObserver>().NotifyAccessChangedAsync(
                        new AccessChange { Kind = AccessChangeKinds.Grant, Reason = "conformance.simulated", TenantId = platform.CompanyId },
                        ct: ct).ConfigureAwait(false);
                }

                acknowledged = await platform.WaitForAsync(
                    all => Revocations(all.Skip(start)).Any(c => c.Status is >= 200 and < 300 && Signal(c).AppInstanceId == platform.AppInstanceId),
                    options.RevocationTimeout,
                    ct).ConfigureAwait(false);
            }
            finally
            {
                platform.ClearFailures();
            }

            calls = [.. Revocations(platform.Calls.Skip(start))];
            Require(acknowledged, $"no acknowledged revocation signal for {platform.AppInstanceId} within {options.RevocationTimeout.TotalSeconds:0} s ({calls.Count} attempts)");

            var call = calls.First(c => c.Status is >= 200 and < 300 && Signal(c).AppInstanceId == platform.AppInstanceId);
            Require(call.Method == "POST", $"sent with {call.Method}");
            Require(call.Authorization == "ApiKey " + platform.ConnectorApiKey, "not sent with the app's platform API key");
            var signal = Signal(call);
            Require(!string.IsNullOrWhiteSpace(signal.RevocationKey), "no revocation key");
            Require(_scope is null || signal.RevocationKey == _scope.RevocationKey, $"revocation key {signal.RevocationKey}, the scope said {_scope?.RevocationKey}");
            Require(!string.IsNullOrWhiteSpace(signal.Reason), "no reason");
            Require(signal.OccurredAt != default, "no occurredAt");
        }).ConfigureAwait(false);

        Check(category, "a refused signal is retried with the same payload until acknowledged", () =>
        {
            if (!acknowledged)
                throw new NotApplicableException("no signal was acknowledged");
            var refused = calls.FirstOrDefault(c => c.Status >= 300) ?? throw new CheckFailedException("no refused attempt was made");
            var key = Signal(refused).RevocationKey;
            var attempts = calls.Where(c => Signal(c).RevocationKey == key).ToList();
            Require(attempts.Count(c => c.Status >= 300) >= 2 && attempts[^1].Status is >= 200 and < 300,
                $"{attempts.Count} attempts for {key}, the last answered {attempts[^1].Status}");
            Require(attempts.All(a => a.Body == attempts[0].Body), "the retries changed the payload");
        });
    }

    private static IEnumerable<AiPlatformCall> Revocations(IEnumerable<AiPlatformCall> calls)
        => calls.Where(c => c.Path.EndsWith("/revocations/scope", StringComparison.Ordinal));

    private static RevocationSignal Signal(AiPlatformCall call)
        => JsonSerializer.Deserialize<RevocationSignal>(call.Body, ConnectorJson.Options)
            ?? throw new CheckFailedException("an empty revocation signal");

    // ── Timeout is deny ─────────────────────────────────────────────

    private Task TimeoutIsDenyAsync()
        => CheckAsync(AiConformanceCategory.TimeoutIsDeny, "a call that outlives the timeout fails with a typed error and no data", async () =>
        {
            var slow = options.SlowCapability ?? throw new NotApplicableException("no slow capability (AiConformanceOptions.SlowCapability)");
            var answer = await Execute(slow, "{}").ConfigureAwait(false);

            Require(answer.Status >= 400, $"the slow capability answered {answer.Status} (it should have timed out; is Ai:Connector:CallTimeout shorter than it?)");
            Require(answer.Error is not null, "the timeout was not a typed error");
            Require(answer.Error!.Code == ConnectorErrorCodes.Unavailable, $"the timeout was answered '{answer.Error.Code}', expected {ConnectorErrorCodes.Unavailable}");
        });

    // ── No adapter-side caching ─────────────────────────────────────

    private Task NoAdapterCachingAsync()
        => CheckAsync(AiConformanceCategory.NoAdapterCaching, "a permission change shows on the next scope call", async () =>
        {
            var change = options.ChangeUserAccess ?? throw new NotApplicableException("no access change (AiConformanceOptions.ChangeUserAccess)");
            var before = await SendAsync(HttpMethod.Post, "/authz/scope", Envelope(User), "{}").ConfigureAwait(false);
            ExpectOk(before, "scope before the change");
            await change(services, ct).ConfigureAwait(false);

            var after = await SendAsync(HttpMethod.Post, "/authz/scope", Envelope(User), "{}").ConfigureAwait(false);
            if (after.Status == 401 && after.Error?.Code == ConnectorErrorCodes.Denied)
                return; // The change locked the user out: reflected.
            ExpectOk(after, "scope after the change");
            var (was, now) = (before.Read<AccessScope>(), after.Read<AccessScope>());
            Require(!was.Permissions.SequenceEqual(now.Permissions) || !was.Roles.SequenceEqual(now.Roles)
                    || !was.FieldPolicies.OrderBy(p => p.Key, StringComparer.Ordinal).SequenceEqual(now.FieldPolicies.OrderBy(p => p.Key, StringComparer.Ordinal)),
                "the scope after the access change is the scope before it (a cache answered)");
        });

    // ── Capabilities ────────────────────────────────────────────────

    // The capabilities the user may call: every required permission is in the scope.
    private IEnumerable<ManifestCapability> Usable()
    {
        var permissions = Scope.Permissions.ToHashSet(StringComparer.Ordinal);
        // The slow capability is only for the timeout check: calling it elsewhere would just wait for the timeout.
        return Manifest.Capabilities.Where(c => c.RequiredPermissions.All(permissions.Contains) && c.Name != options.SlowCapability);
    }

    private static JsonObject? Properties(ManifestCapability capability) => capability.InputSchema["properties"] as JsonObject;

    private static IReadOnlyList<string> Required(ManifestCapability capability)
        => capability.InputSchema["required"] is JsonArray required ? [.. required.Select(r => (string?)r).OfType<string>()] : [];

    // The smallest arguments the schema accepts: {} unless something is required; a generated Calculate needs an aggregate.
    private static string? MinimalArguments(ManifestCapability capability)
    {
        var required = Required(capability);
        if (required.Count == 0)
            return "{}";
        return required.SequenceEqual(["aggregate"]) ? """{"aggregate":"count"}""" : null;
    }

    // The probe's arguments plus whatever the schema requires (count for a generated Calculate).
    private static string Arguments(ManifestCapability capability, JsonObject args)
    {
        if (Required(capability).Contains("aggregate") && !args.ContainsKey("aggregate"))
            args["aggregate"] = "count";
        return args.ToJsonString();
    }

    private static IEnumerable<string> StringProperties(ManifestCapability capability)
        => Properties(capability) is { } properties
            ? properties.Where(p => p.Value is JsonObject schema && IsString(schema["type"])).Select(p => p.Key)
            : [];

    private static bool IsString(JsonNode? type)
        => type switch
        {
            JsonValue value => (string?)value == "string",
            JsonArray types => types.Any(t => (string?)t == "string"),
            _ => false,
        };

    // The fields a generated Search or Calculate filters on (its filters' field enum).
    private static IReadOnlyList<string> FilterFields(ManifestCapability capability)
        => Properties(capability)?["filters"]?["items"]?["properties"]?["field"]?["enum"] is JsonArray fields
            ? [.. fields.Select(f => (string?)f).OfType<string>()]
            : [];

    // ── HTTP ────────────────────────────────────────────────────────

    private Task<Answer> Execute(string capability, string args)
        => SendAsync(HttpMethod.Post, $"/capabilities/{Uri.EscapeDataString(capability)}:execute", Envelope(User), $$"""{"args":{{args}}}""");

    private Task<Answer> Execute(string capability, JsonObject? args)
        => Execute(capability, args?.ToJsonString() ?? "{}");

    private Task<Answer> GetResource(string type, string id)
        => SendAsync(HttpMethod.Post, "/resources:get", Envelope(User), JsonSerializer.Serialize(new ResourceGetRequest(type, id), ConnectorJson.Options));

    private string Envelope(string user, Action<Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor>? tweak = null)
        => platform.Envelope(user, userClaim: _connector.Users.Claim, tweak: tweak);

    private HttpRequestMessage Request(HttpMethod method, string path, string? envelope = null, string? json = null, string? apiKey = null)
        => platform.Request(method, path, envelope, json, apiKey, _connector.PathPrefix);

    private Task<Answer> SendAsync(HttpMethod method, string path, string? envelope = null, string? json = null, string? apiKey = null)
        => SendOwnedAsync(Request(method, path, envelope, json, apiKey));

    private async Task<Answer> SendOwnedAsync(HttpRequestMessage request)
    {
        using (request)
            return await SendAsync(request).ConfigureAwait(false);
    }

    private async Task<Answer> SendAsync(HttpRequestMessage request)
    {
        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        var answer = new Answer((int)response.StatusCode, await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (answer.Status >= 400 && answer.Error is null)
            _untypedErrors.Add($"{request.Method} {request.RequestUri?.OriginalString.Split('?')[0]} answered {answer.Describe()}");
        return answer;
    }

    // ── Check plumbing ──────────────────────────────────────────────

    private async Task CheckAsync(AiConformanceCategory category, string check, Func<Task> body)
    {
        try
        {
            await body().ConfigureAwait(false);
            _results.Add(new AiConformanceResult(category, check, AiConformanceOutcome.Passed));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _results.Add(Outcome(category, check, ex));
        }
    }

    private void Check(AiConformanceCategory category, string check, Action body)
    {
        try
        {
            body();
            _results.Add(new AiConformanceResult(category, check, AiConformanceOutcome.Passed));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _results.Add(Outcome(category, check, ex));
        }
    }

    // A check that could not run is not applicable; any other exception, expected or not, fails it.
    private static AiConformanceResult Outcome(AiConformanceCategory category, string check, Exception ex)
        => ex switch
        {
            NotApplicableException => new AiConformanceResult(category, check, AiConformanceOutcome.NotApplicable, ex.Message),
            CheckFailedException => new AiConformanceResult(category, check, AiConformanceOutcome.Failed, ex.Message),
            _ => new AiConformanceResult(category, check, AiConformanceOutcome.Failed, $"{ex.GetType().Name}: {ex.Message}"),
        };

    private void ExpectError(Answer answer, int status, string code, string? what = null)
        => Require(answer.Status == status && answer.Error?.Code == code,
            $"{(what is null ? string.Empty : what + ": ")}expected {status} {code}, got {answer.Describe()}");

    private static void ExpectOk(Answer answer, string? what = null)
        => Require(answer.Status == 200, $"{(what is null ? string.Empty : what + ": ")}expected 200, got {answer.Describe()}");

    private static void Require(bool condition, string failure)
    {
        if (!condition)
            throw new CheckFailedException(failure);
    }

    private sealed record Answer(int Status, string Body)
    {
        public ConnectorError? Error
        {
            get
            {
                try
                {
                    return JsonSerializer.Deserialize<ConnectorError>(Body, ConnectorJson.Options) is { Code: { } code } error && KnownCodes.Contains(code) ? error : null;
                }
                catch (JsonException)
                {
                    return null;
                }
            }
        }

        public T Read<T>()
            => JsonSerializer.Deserialize<T>(Body, ConnectorJson.Options) ?? throw new CheckFailedException($"an empty {typeof(T).Name}");

        public string Describe()
            => Error is { } error ? $"{Status} {error.Code}" : $"{Status} {(Body.Length > 120 ? Body[..120] + "…" : Body)}".TrimEnd();
    }

    private sealed class CheckFailedException(string message) : Exception(message);

    private sealed class NotApplicableException(string message) : Exception(message);
}
