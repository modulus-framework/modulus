using System.Text.Json;
using System.Text.Json.Serialization;

namespace Modulus.Identity.Scim;

/// <summary>SCIM schema URNs.</summary>
internal static class ScimSchemas
{
    /// <summary>The core User schema.</summary>
    public const string User = "urn:ietf:params:scim:schemas:core:2.0:User";

    /// <summary>The core Group schema.</summary>
    public const string Group = "urn:ietf:params:scim:schemas:core:2.0:Group";

    /// <summary>A list response.</summary>
    public const string ListResponse = "urn:ietf:params:scim:api:messages:2.0:ListResponse";

    /// <summary>A PATCH request.</summary>
    public const string PatchOp = "urn:ietf:params:scim:api:messages:2.0:PatchOp";

    /// <summary>An error response.</summary>
    public const string Error = "urn:ietf:params:scim:api:messages:2.0:Error";

    /// <summary>The service provider configuration.</summary>
    public const string ServiceProviderConfig = "urn:ietf:params:scim:schemas:core:2.0:ServiceProviderConfig";

    /// <summary>The resource type description.</summary>
    public const string ResourceType = "urn:ietf:params:scim:schemas:core:2.0:ResourceType";
}

/// <summary>The SCIM media type.</summary>
internal static class ScimMediaTypes
{
    /// <summary><c>application/scim+json</c>.</summary>
    public const string Json = "application/scim+json";
}

/// <summary>Name of a user.</summary>
internal sealed class ScimName
{
    /// <summary>Full name.</summary>
    [JsonPropertyName("formatted")] public string? Formatted { get; set; }

    /// <summary>Family name.</summary>
    [JsonPropertyName("familyName")] public string? FamilyName { get; set; }

    /// <summary>Given name.</summary>
    [JsonPropertyName("givenName")] public string? GivenName { get; set; }
}

/// <summary>An e-mail address.</summary>
internal sealed class ScimEmail
{
    /// <summary>The address.</summary>
    [JsonPropertyName("value")] public string? Value { get; set; }

    /// <summary>Kind, for example <c>work</c>.</summary>
    [JsonPropertyName("type")] public string? Type { get; set; }

    /// <summary>Whether it is the main address.</summary>
    [JsonPropertyName("primary")] public bool? Primary { get; set; }
}

/// <summary>A reference to another resource (a group of a user, a member of a group).</summary>
internal sealed class ScimRef
{
    /// <summary>The resource id.</summary>
    [JsonPropertyName("value")] public string? Value { get; set; }

    /// <summary>A display name.</summary>
    [JsonPropertyName("display")] public string? Display { get; set; }

    /// <summary>The resource's type, <c>User</c> or <c>Group</c>.</summary>
    [JsonPropertyName("type")] public string? Type { get; set; }

    /// <summary>The resource's URL.</summary>
    [JsonPropertyName("$ref")] public string? Ref { get; set; }
}

/// <summary>Resource metadata.</summary>
internal sealed class ScimMeta
{
    /// <summary><c>User</c> or <c>Group</c>.</summary>
    [JsonPropertyName("resourceType")] public string? ResourceType { get; set; }

    /// <summary>The resource's URL.</summary>
    [JsonPropertyName("location")] public string? Location { get; set; }
}

/// <summary>A SCIM User, as sent by a client and as returned.</summary>
internal sealed class ScimUser
{
    /// <summary>Schemas.</summary>
    [JsonPropertyName("schemas")] public List<string> Schemas { get; set; } = [ScimSchemas.User];

    /// <summary>Id (server-assigned).</summary>
    [JsonPropertyName("id")] public string? Id { get; set; }

    /// <summary>The client's own id for the person.</summary>
    [JsonPropertyName("externalId")] public string? ExternalId { get; set; }

    /// <summary>Unique user name.</summary>
    [JsonPropertyName("userName")] public string? UserName { get; set; }

    /// <summary>Name parts.</summary>
    [JsonPropertyName("name")] public ScimName? Name { get; set; }

    /// <summary>Display name (computed from the name parts; a value sent by the client is ignored).</summary>
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }

    /// <summary>E-mail addresses.</summary>
    [JsonPropertyName("emails")] public List<ScimEmail>? Emails { get; set; }

    /// <summary>Whether the person may sign in.</summary>
    [JsonPropertyName("active")] public bool? Active { get; set; }

    /// <summary>Groups (roles) the person is in; read-only.</summary>
    [JsonPropertyName("groups")] public List<ScimRef>? Groups { get; set; }

    /// <summary>Metadata.</summary>
    [JsonPropertyName("meta")] public ScimMeta? Meta { get; set; }
}

/// <summary>A SCIM Group (a role of the company).</summary>
internal sealed class ScimGroup
{
    /// <summary>Schemas.</summary>
    [JsonPropertyName("schemas")] public List<string> Schemas { get; set; } = [ScimSchemas.Group];

    /// <summary>Id (the role id).</summary>
    [JsonPropertyName("id")] public string? Id { get; set; }

    /// <summary>The client's own id.</summary>
    [JsonPropertyName("externalId")] public string? ExternalId { get; set; }

    /// <summary>The role name.</summary>
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }

    /// <summary>Members (users).</summary>
    [JsonPropertyName("members")] public List<ScimRef>? Members { get; set; }

    /// <summary>Metadata.</summary>
    [JsonPropertyName("meta")] public ScimMeta? Meta { get; set; }
}

/// <summary>One PATCH operation.</summary>
internal sealed class ScimPatchOperation
{
    /// <summary><c>add</c>, <c>remove</c> or <c>replace</c> (any case).</summary>
    [JsonPropertyName("op")] public string? Op { get; set; }

    /// <summary>The attribute path; null when <see cref="Value"/> is an object of attributes.</summary>
    [JsonPropertyName("path")] public string? Path { get; set; }

    /// <summary>The value.</summary>
    [JsonPropertyName("value")] public JsonElement? Value { get; set; }
}

/// <summary>A PATCH request.</summary>
internal sealed class ScimPatchRequest
{
    /// <summary>Schemas.</summary>
    [JsonPropertyName("schemas")] public List<string>? Schemas { get; set; }

    /// <summary>The operations (clients spell it <c>Operations</c>).</summary>
    [JsonPropertyName("Operations")] public List<ScimPatchOperation>? Operations { get; set; }
}

/// <summary>A list response.</summary>
/// <typeparam name="T">The resource type.</typeparam>
internal sealed class ScimListResponse<T>
{
    /// <summary>Schemas.</summary>
    [JsonPropertyName("schemas")] public List<string> Schemas { get; set; } = [ScimSchemas.ListResponse];

    /// <summary>Matches in total.</summary>
    [JsonPropertyName("totalResults")] public int TotalResults { get; set; }

    /// <summary>1-based index of the first resource returned.</summary>
    [JsonPropertyName("startIndex")] public int StartIndex { get; set; }

    /// <summary>How many resources this page holds.</summary>
    [JsonPropertyName("itemsPerPage")] public int ItemsPerPage { get; set; }

    /// <summary>The resources.</summary>
    [JsonPropertyName("Resources")] public List<T> Resources { get; set; } = [];
}

/// <summary>A SCIM error.</summary>
internal sealed class ScimError
{
    /// <summary>Schemas.</summary>
    [JsonPropertyName("schemas")] public List<string> Schemas { get; set; } = [ScimSchemas.Error];

    /// <summary>The HTTP status, as text.</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = "";

    /// <summary>A SCIM error type (<c>invalidFilter</c>, <c>uniqueness</c>, <c>invalidValue</c>, <c>mutability</c>, <c>noTarget</c>, <c>invalidPath</c>).</summary>
    [JsonPropertyName("scimType")] public string? ScimType { get; set; }

    /// <summary>What went wrong.</summary>
    [JsonPropertyName("detail")] public string? Detail { get; set; }
}

/// <summary>A failure the endpoints turn into a SCIM error response.</summary>
internal sealed class ScimException(int status, string? scimType, string detail) : Exception(detail)
{
    public int Status { get; } = status;

    public string? ScimType { get; } = scimType;
}
