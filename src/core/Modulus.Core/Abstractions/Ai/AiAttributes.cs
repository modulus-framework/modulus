namespace Modulus.Core.Abstractions.Ai;

/// <summary>
/// Exposes a read-only mediator query to an external AI platform as a named capability (Modulus.AI.Connector).
/// Exposure is opt-in: a query without this attribute is never offered, and the connector refuses the attribute
/// on a command at startup, because the platform is read-only. The query runs through the normal mediator
/// pipeline as the asserted user, so its <c>[RequirePermission]</c>, <c>[RequireFeature]</c>, validation and the
/// tenant filter all apply.
/// <code>
/// [AiCapability("Erp.Catalog.Product.Search", "Finds products by name or SKU.", ResourceType = "Catalog.Product")]
/// [RequirePermission("catalog:products:manage")]
/// public sealed record SearchProductsQuery(string? Text) : IQuery&lt;IReadOnlyList&lt;ProductDto&gt;&gt;;
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AiCapabilityAttribute : Attribute
{
    /// <summary>Declares the capability.</summary>
    /// <param name="name">The capability name, <c>{App}.{Module}.{Entity}.{Verb}</c> (letters, digits, dots).</param>
    /// <param name="description">What it does, for the platform's planner. Plain text, length-limited.</param>
    public AiCapabilityAttribute(string name, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Name = name;
        Description = description;
    }

    /// <summary>The capability name.</summary>
    public string Name { get; }

    /// <summary>What the capability does.</summary>
    public string Description { get; }

    /// <summary>
    /// The resource type each result item is (an <see cref="AiResourceAttribute.ResourceType"/>), so items carry a
    /// reference and a deep link. Null for results that are not records (totals, counts).
    /// </summary>
    public string? ResourceType { get; set; }
}

/// <summary>
/// Marks the query that looks up one record by id as the lookup of a resource type, for the AI platform's direct
/// lookups and per-record authorization checks. The query must have one public constructor taking the id
/// (<see cref="Guid"/>, <see cref="string"/>, <see cref="int"/> or <see cref="long"/>); it runs as the asserted
/// user like any capability.
/// <code>
/// [AiResource("Catalog.Product", "A product in the catalog.", DeepLink = "/Catalog/Products?id={id}", TitleField = "Name")]
/// public sealed record GetProductQuery(Guid Id) : IQuery&lt;ProductDto&gt;;
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AiResourceAttribute : Attribute
{
    /// <summary>Declares the resource type.</summary>
    /// <param name="resourceType">The type name, e.g. <c>Catalog.Product</c> (letters, digits, dots).</param>
    /// <param name="description">What a record of this type is.</param>
    public AiResourceAttribute(string resourceType, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ResourceType = resourceType;
        Description = description;
    }

    /// <summary>The resource type name.</summary>
    public string ResourceType { get; }

    /// <summary>What a record of this type is.</summary>
    public string Description { get; }

    /// <summary>
    /// The page that shows one record; <c>{id}</c> is replaced by the URL-encoded id. A relative link is
    /// prefixed with the connector's public base URL.
    /// </summary>
    public string? DeepLink { get; set; }

    /// <summary>The result property that names the record (shown as its title).</summary>
    public string? TitleField { get; set; }

    /// <summary>
    /// An optional batch version of the lookup, used by <c>GET /extract</c> and <c>GET /changes</c> to read a page of records in one
    /// query instead of one query each: an <c>IQuery&lt;T&gt;</c> whose single public constructor takes the ids (an array,
    /// <c>IReadOnlyCollection&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c> or <c>List&lt;T&gt;</c> of the lookup's
    /// id type) and that returns the same record type, each carrying its <c>Id</c>. It runs through the mediator as the indexing identity
    /// exactly like the single lookup (permission, filters, soft-delete); an id it does not return is treated as not found.
    /// </summary>
    public Type? BatchLookup { get; set; }
}

/// <summary>
/// Marks an entity whose records the AI platform may index (Modulus.AI.Connector). Its inserts, updates and deletes
/// are journaled in the same transaction as the entity (the change feed, <c>GET /changes</c>), and its keys are paged
/// by <c>GET /extract</c>. The records themselves are always served by the resource type's
/// <see cref="AiResourceAttribute"/> lookup, which the connector requires, so the index sees exactly the fields a
/// direct lookup returns. The entity needs one client-generated key (<see cref="Guid"/>, <see cref="string"/>,
/// <see cref="int"/> or <see cref="long"/>): a database-generated key does not exist yet when the journal is written.
/// <code>
/// [AiIndexed("Catalog.Product")]
/// public sealed class Product : AuditableEntity { ... }
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AiIndexedAttribute : Attribute
{
    /// <summary>Declares the entity indexable as <paramref name="resourceType"/>.</summary>
    /// <param name="resourceType">The resource type (an <see cref="AiResourceAttribute.ResourceType"/>).</param>
    public AiIndexedAttribute(string resourceType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        ResourceType = resourceType;
    }

    /// <summary>The resource type the entity's records are.</summary>
    public string ResourceType { get; }
}

/// <summary>
/// Gives an entity two generated AI capabilities (Modulus.AI.Connector): <c>{ResourceType}.Search</c>, which filters
/// and sorts on the <see cref="Fields"/> only and returns those fields, and <c>{ResourceType}.Calculate</c>, which
/// counts, sums, averages or finds the minimum or maximum of a numeric field, optionally grouped by one field, so
/// figures come from the database rather than from text a model reads. Filters are translated to expressions over the
/// listed properties, never to SQL text, and run through the entity's normal query filters (company, soft delete,
/// organization scope). Both capabilities require <see cref="Permission"/>.
/// <code>
/// [AiQueryable("Catalog.Product", "Products in the catalog.", "catalog:products:read", Fields = ["Name", "Sku", "Price", "CreatedAt"])]
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AiQueryableAttribute : Attribute
{
    /// <summary>Declares the generated capabilities.</summary>
    /// <param name="resourceType">The resource type; the capabilities are named after it (at least two segments).</param>
    /// <param name="description">What the records are, for the platform's planner.</param>
    /// <param name="permission">The permission both capabilities require.</param>
    public AiQueryableAttribute(string resourceType, string description, string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        ResourceType = resourceType;
        Description = description;
        Permission = permission;
    }

    /// <summary>The resource type.</summary>
    public string ResourceType { get; }

    /// <summary>What the records are.</summary>
    public string Description { get; }

    /// <summary>The permission both capabilities require.</summary>
    public string Permission { get; }

    /// <summary>
    /// The properties the platform may filter, sort, group and aggregate on, and the only ones a search returns
    /// (besides the key). Scalar properties only; a <c>[SecretData]</c> property is refused at startup.
    /// </summary>
    public string[] Fields { get; set; } = [];
}
