namespace Modulus.Identity.Scim;

/// <summary>One attribute of a SCIM schema (RFC 7643 §7). Serialized as it appears in a schema document.</summary>
internal sealed record ScimAttributeDoc(
    string Name,
    string Type,
    bool MultiValued,
    bool Required,
    string Mutability,
    string Returned,
    string Uniqueness,
    string? Description = null,
    IReadOnlyList<ScimAttributeDoc>? SubAttributes = null);

/// <summary>A SCIM schema document (RFC 7643 §6): what the server accepts and returns for a resource type.</summary>
internal sealed record ScimSchemaDocument(
    string[] Schemas,
    string Id,
    string Name,
    string Description,
    IReadOnlyList<ScimAttributeDoc> Attributes,
    ScimMeta Meta);

/// <summary>The User and Group schema documents this server serves at <c>/Schemas</c>.</summary>
internal static class ScimSchemaDocuments
{
    private const string SchemaUrn = "urn:ietf:params:scim:schemas:core:2.0:Schema";

    public static IReadOnlyList<ScimSchemaDocument> All(string baseUrl) => [User(baseUrl), Group(baseUrl)];

    public static ScimSchemaDocument? Find(string id, string baseUrl)
        => All(baseUrl).FirstOrDefault(doc => string.Equals(doc.Id, id, StringComparison.Ordinal));

    private static ScimSchemaDocument User(string baseUrl) => Document(
        ScimSchemas.User, "User", "User account of the company.", baseUrl,
        Attr("id", "string", mutability: "readOnly", returned: "always", uniqueness: "server", description: "Unique identifier of the account."),
        Attr("externalId", "string", description: "The identifier the provisioning client knows the account by."),
        Attr("userName", "string", required: true, uniqueness: "server", description: "The sign-in name; unique in the server."),
        Attr("name", "complex", subAttributes:
        [
            Attr("givenName", "string"),
            Attr("familyName", "string"),
        ]),
        Attr("emails", "complex", multiValued: true, subAttributes:
        [
            Attr("value", "string"),
            Attr("type", "string"),
            Attr("primary", "boolean"),
        ]),
        Attr("active", "boolean", description: "False when the account may not start new access."),
        Attr("groups", "complex", multiValued: true, mutability: "readOnly", subAttributes:
        [
            Attr("value", "string", mutability: "readOnly"),
            Attr("display", "string", mutability: "readOnly"),
            Attr("$ref", "reference", mutability: "readOnly"),
            Attr("type", "string", mutability: "readOnly"),
        ]));

    private static ScimSchemaDocument Group(string baseUrl) => Document(
        ScimSchemas.Group, "Group", "A role of the company, exposed as a group.", baseUrl,
        Attr("id", "string", mutability: "readOnly", returned: "always", uniqueness: "server"),
        Attr("displayName", "string", required: true, description: "The role's name; a group is never renamed."),
        Attr("members", "complex", multiValued: true, subAttributes:
        [
            Attr("value", "string"),
            Attr("display", "string", mutability: "readOnly"),
            Attr("$ref", "reference", mutability: "readOnly"),
            Attr("type", "string", mutability: "readOnly"),
        ]));

    private static ScimSchemaDocument Document(string id, string name, string description, string baseUrl, params ScimAttributeDoc[] attributes)
        => new(
            [SchemaUrn],
            id,
            name,
            description,
            attributes,
            new ScimMeta { ResourceType = "Schema", Location = $"{baseUrl}/Schemas/{id}" });

    private static ScimAttributeDoc Attr(
        string name,
        string type,
        bool multiValued = false,
        bool required = false,
        string mutability = "readWrite",
        string returned = "default",
        string uniqueness = "none",
        string? description = null,
        IReadOnlyList<ScimAttributeDoc>? subAttributes = null)
        => new(name, type, multiValued, required, mutability, returned, uniqueness, description, subAttributes);
}
