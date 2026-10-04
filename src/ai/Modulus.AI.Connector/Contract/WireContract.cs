namespace Modulus.AI.Connector.Contract;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

// The shapes of wire contract v1 as this app serves them. They follow the platform's published records
// (Architecture §4: ResourceReference, AppResource, CapabilityDescriptor, AppAccessScope; Integration Guide §9) and
// stay in this one folder, so they can be regenerated from the platform's Integrations.Contracts OpenAPI document
// without touching the rest of the package. JSON is camelCase; enums are their names.

/// <summary>The manifest: what this app offers the platform (<c>GET /manifest</c>).</summary>
public sealed record ConnectorManifest(
    string ContractVersion,
    string AppType,
    string? AppName,
    string Fingerprint,
    IReadOnlyList<ManifestCapability> Capabilities,
    IReadOnlyList<ManifestResourceType> ResourceTypes);

/// <summary>One capability: a read-only query the platform's executor may call.</summary>
public sealed record ManifestCapability(
    string Name,
    string Description,
    bool ReadOnly,
    JsonNode InputSchema,
    IReadOnlyList<string> RequiredPermissions,
    string? ResourceType,
    IReadOnlyList<ManifestField> OutputFields);

/// <summary>
/// One resource type: records the platform may look up and link to, and, when <paramref name="Indexed"/>, extract and
/// follow through <c>/extract</c> and <c>/changes</c>.
/// </summary>
public sealed record ManifestResourceType(
    string Type,
    string Description,
    string? DeepLink,
    string? TitleField,
    bool Indexed,
    IReadOnlyList<ManifestField> Fields);

/// <summary>One field with its classification. Secret fields are never listed.</summary>
public sealed record ManifestField(string Name, string Type, AiDataClass Classification);

/// <summary>Names one record.</summary>
public sealed record ResourceReference(string ResourceType, string ResourceId, string? Namespace = null);

/// <summary>One record, with only the fields the user may see.</summary>
public sealed record AppResource(
    ResourceReference? Reference,
    IReadOnlyDictionary<string, JsonElement> Fields,
    string? DeepLink);

/// <summary>
/// A record for the platform's index (<c>/extract</c>, <c>/changes</c>): the fields the indexing identity may read, and
/// the access metadata the platform pre-filters on. Every answer built from it is still re-authorized per user.
/// </summary>
public sealed record IndexedResource(
    ResourceReference Reference,
    IReadOnlyDictionary<string, JsonElement> Fields,
    string? DeepLink,
    ResourceAccess Access);

/// <summary>Who may see a record: any of the permissions, within the data scopes (<c>company</c>).</summary>
public sealed record ResourceAccess(IReadOnlyList<string> RequiredPermissions, IReadOnlyDictionary<string, string[]> DataScopes);

/// <summary>A page of <c>GET /extract</c>; a null <paramref name="NextCursor"/> means the extraction is complete.</summary>
public sealed record ExtractPage(IReadOnlyList<IndexedResource> Resources, string? NextCursor);

/// <summary>What a change does to the index.</summary>
public enum ResourceChangeKind
{
    /// <summary>Index (or re-index) the record.</summary>
    Upsert,

    /// <summary>Remove the record: deleted, or no longer visible to the indexing identity (re-scoped).</summary>
    Tombstone,
}

/// <summary>One change of <c>GET /changes</c>; <paramref name="Resource"/> is set for an upsert only.</summary>
public sealed record ResourceChange(
    ResourceChangeKind Kind,
    ResourceReference Reference,
    IndexedResource? Resource,
    DateTimeOffset OccurredAt);

/// <summary>A page of <c>GET /changes</c>. <paramref name="Cursor"/> is where the next call resumes (pass it as <c>since</c>).</summary>
public sealed record ChangesPage(IReadOnlyList<ResourceChange> Changes, string Cursor, bool HasMore);

/// <summary>
/// The body of a change hint: "this app instance changed", never data (AD-27). The platform answers it by calling
/// <c>/changes</c>.
/// </summary>
public sealed record ChangeHint(string AppInstanceId, string EventId, DateTimeOffset OccurredAt);

/// <summary>The body of <c>POST /capabilities/{name}:execute</c>.</summary>
public sealed record CapabilityExecuteRequest(JsonElement? Args);

/// <summary>The answer of a capability: its records, and whether <see cref="ModulusAiConnectorOptions.MaxResults"/> cut them.</summary>
public sealed record CapabilityResult(IReadOnlyList<AppResource> Resources, bool Truncated);

/// <summary>The body of <c>POST /resources:get</c>.</summary>
public sealed record ResourceGetRequest(string ResourceType, string ResourceId);

/// <summary>The body of <c>POST /authz/scope</c> (the user is the envelope's; the body may be empty).</summary>
public sealed record ScopeRequest(string? AppInstanceId = null);

/// <summary>What the asserted user may do in this app (AppAccessScope). Never cached here (AD-13).</summary>
public sealed record AccessScope(
    string AppInstanceId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    IReadOnlyDictionary<string, string[]> DataScopes,
    IReadOnlyDictionary<string, FieldAccessPolicy> FieldPolicies,
    int TtlSeconds,
    string RevocationKey);

/// <summary>A field's policy in a scope.</summary>
public enum FieldAccessPolicy
{
    /// <summary>The user may see it.</summary>
    Allow,

    /// <summary>The user may not see it.</summary>
    Deny,
}

/// <summary>The body of <c>POST /authz/resources:check</c>.</summary>
public sealed record ResourcesCheckRequest(IReadOnlyList<ResourceReference> Resources);

/// <summary>The answer of <c>POST /authz/resources:check</c>, in request order.</summary>
public sealed record ResourcesCheckResult(IReadOnlyList<ResourceDecision> Decisions);

/// <summary>Whether the user may see one record.</summary>
public sealed record ResourceDecision(ResourceReference Reference, bool Allowed);

/// <summary>The body of <c>POST /authz/fields:check</c>.</summary>
public sealed record FieldsCheckRequest(ResourceReference Resource, IReadOnlyList<string> Fields);

/// <summary>The fields of the request the user may see.</summary>
public sealed record FieldsCheckResult(IReadOnlyList<string> AllowedFields);

/// <summary>The answer of <c>GET /health</c>.</summary>
public sealed record ConnectorHealth(string Status, string ContractVersion);

/// <summary>A typed error. The platform retries <c>UNAVAILABLE</c> and <c>RATE_LIMITED</c>, never <c>DENIED</c>.</summary>
public sealed record ConnectorError(string Code, string Message);

/// <summary>The body of the platform's <c>POST /revocations/scope</c>; the same payload on every retry.</summary>
public sealed record RevocationSignal(string RevocationKey, string AppInstanceId, string Reason, DateTimeOffset OccurredAt);

/// <summary>The typed error codes.</summary>
public static class ConnectorErrorCodes
{
    /// <summary>Not allowed (final; never retried). Also the answer to any authentication failure.</summary>
    public const string Denied = "DENIED";

    /// <summary>No such capability, resource type or record (or the user may not know it exists).</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>Temporarily unable to answer: a timeout or a failing dependency. Retryable.</summary>
    public const string Unavailable = "UNAVAILABLE";

    /// <summary>Too many calls. Retryable after a back-off.</summary>
    public const string RateLimited = "RATE_LIMITED";

    /// <summary>
    /// The arguments do not match the capability's input schema. Not one of the four codes the platform documents;
    /// it treats an unknown code as a deny.
    /// </summary>
    public const string InvalidRequest = "INVALID_REQUEST";
}

/// <summary>The serializer settings of the wire contract.</summary>
public static class ConnectorJson
{
    /// <summary>
    /// The contract's own bodies: camelCase, enum names, nulls written (a null field is an answer too), unknown members
    /// ignored (a later minor version of the same major may add some).
    /// </summary>
    public static JsonSerializerOptions Options { get; } = Create(JsonUnmappedMemberHandling.Skip);

    /// <summary>
    /// A capability's arguments, bound to the query: the same, except that an unknown member is refused, so the
    /// planner cannot pass anything the input schema does not declare.
    /// </summary>
    public static JsonSerializerOptions Arguments { get; } = Create(JsonUnmappedMemberHandling.Disallow);

    private static JsonSerializerOptions Create(JsonUnmappedMemberHandling unmapped)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = unmapped,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();
        options.MakeReadOnly();
        return options;
    }
}
