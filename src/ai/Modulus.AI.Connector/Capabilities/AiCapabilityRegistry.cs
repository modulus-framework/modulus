namespace Modulus.AI.Connector.Capabilities;

using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.RegularExpressions;
using Modulus.AI.Connector.Contract;
using Modulus.Core.Abstractions.Ai;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

/// <summary>
/// A capability: an annotated query, or one generated from an <see cref="AiQueryableAttribute"/> entity
/// (<paramref name="Queryable"/> set; <paramref name="QueryType"/> is then the entity).
/// </summary>
internal sealed record AiCapabilityDescriptor(
    string Name,
    string Description,
    Type QueryType,
    Type ResponseType,
    Type ItemType,
    string? RequiredPermission,
    string? ResourceType,
    JsonNode InputSchema,
    AiQueryableDescriptor? Queryable = null,
    AiGeneratedCapability Generated = AiGeneratedCapability.None,
    IReadOnlyList<AiField>? OutputFields = null)
{
    /// <summary>The fields one result record has.</summary>
    public IReadOnlyList<AiField> Fields => OutputFields ?? AiFieldCatalog.For(ItemType);
}

/// <summary>Which generated capability a descriptor is.</summary>
internal enum AiGeneratedCapability
{
    /// <summary>An annotated query.</summary>
    None,

    /// <summary><c>{ResourceType}.Search</c>.</summary>
    Search,

    /// <summary><c>{ResourceType}.Calculate</c>.</summary>
    Calculate,
}

/// <summary>An <see cref="AiQueryableAttribute"/> entity: its whitelisted fields, by wire name.</summary>
internal sealed record AiQueryableDescriptor(
    string ResourceType,
    Type EntityType,
    string Permission,
    IReadOnlyDictionary<string, AiField> Fields,
    AiField? Key);

/// <summary>An <see cref="AiIndexedAttribute"/> entity and the lookup that serves its records.</summary>
internal sealed record AiIndexedDescriptor(string ResourceType, Type EntityType, AiResourceDescriptor Resource);

/// <summary>The lookup query of a resource type.</summary>
internal sealed record AiResourceDescriptor(
    string ResourceType,
    string Description,
    Type QueryType,
    Type ResponseType,
    Type ItemType,
    ConstructorInfo Constructor,
    Type IdType,
    string? RequiredPermission,
    string? DeepLink,
    string? TitleField,
    AiBatchLookup? Batch = null);

/// <summary>The batch version of a resource lookup: one query for many ids (see <see cref="AiResourceAttribute.BatchLookup"/>).</summary>
/// <param name="Constructor">The query's constructor, taking the ids.</param>
/// <param name="ResponseType">The query's response type.</param>
/// <param name="ListType">How the constructor takes the ids.</param>
/// <param name="IdProperty">The <c>Id</c> property of a returned record.</param>
internal sealed record AiBatchLookup(ConstructorInfo Constructor, Type ResponseType, Type ListType, PropertyInfo IdProperty);

/// <summary>
/// Every capability and resource type of the app, read from <see cref="AiCapabilityAttribute"/> and
/// <see cref="AiResourceAttribute"/> once at startup and immutable afterwards. Refuses a command (the platform is
/// read-only), a malformed or duplicate name, a description over the length limit, a capability whose resource type
/// has no lookup, and a lookup without a single id constructor.
/// </summary>
internal sealed partial class AiCapabilityRegistry
{
    private static readonly Type[] IdTypes = [typeof(Guid), typeof(string), typeof(int), typeof(long)];

    private static AiBatchLookup BatchOf(Type batchType, string resourceType, Type idType, Type itemType)
    {
        Check(!IsCommand(batchType), $"The batch lookup '{batchType.FullName}' of '{resourceType}' is a command: only queries can be AI lookups.");
        var response = QueryResponseType(batchType);
        Check(response is not null, $"The batch lookup '{batchType.FullName}' of '{resourceType}' is not an IQuery<T>.");

        var constructors = batchType.GetConstructors();
        var parameters = constructors.Length == 1 ? constructors[0].GetParameters() : [];
        var listType = parameters.Length == 1 ? parameters[0].ParameterType : null;
        var accepted = new[]
        {
            idType.MakeArrayType(),
            typeof(IReadOnlyCollection<>).MakeGenericType(idType),
            typeof(IReadOnlyList<>).MakeGenericType(idType),
            typeof(IEnumerable<>).MakeGenericType(idType),
            typeof(List<>).MakeGenericType(idType),
        };
        Check(listType is not null && accepted.Contains(listType),
            $"The batch lookup '{batchType.FullName}' of '{resourceType}' needs one public constructor taking the ids " +
            $"(an array, IReadOnlyCollection, IReadOnlyList, IEnumerable or List of {idType.Name}).");

        var batchItem = AiFieldCatalog.ItemType(response!);
        var id = batchItem.GetProperty("Id");
        Check(batchItem == itemType && id is not null && id.PropertyType == idType,
            $"The batch lookup '{batchType.FullName}' of '{resourceType}' must return the same record type as the single lookup ('{itemType.Name}'), each with a {idType.Name} Id.");
        return new AiBatchLookup(constructors[0], response!, listType!, id!);
    }

    private readonly Dictionary<string, AiCapabilityDescriptor> _capabilities;
    private readonly Dictionary<string, AiResourceDescriptor> _resources;
    private readonly Dictionary<string, AiIndexedDescriptor> _indexed;

    private AiCapabilityRegistry(
        Dictionary<string, AiCapabilityDescriptor> capabilities,
        Dictionary<string, AiResourceDescriptor> resources,
        Dictionary<string, AiIndexedDescriptor> indexed)
    {
        _capabilities = capabilities;
        _resources = resources;
        _indexed = indexed;
    }

    /// <summary>The indexed resource types, by name (the order <c>/extract</c> walks them in).</summary>
    public IEnumerable<AiIndexedDescriptor> Indexed => _indexed.Values.OrderBy(r => r.ResourceType, StringComparer.Ordinal);

    public bool TryGetIndexed(string resourceType, out AiIndexedDescriptor indexed)
        => _indexed.TryGetValue(resourceType, out indexed!);

    /// <summary>The capabilities, by name.</summary>
    public IEnumerable<AiCapabilityDescriptor> Capabilities => _capabilities.Values.OrderBy(c => c.Name, StringComparer.Ordinal);

    /// <summary>The resource types, by name.</summary>
    public IEnumerable<AiResourceDescriptor> Resources => _resources.Values.OrderBy(r => r.ResourceType, StringComparer.Ordinal);

    public bool TryGetCapability(string name, out AiCapabilityDescriptor capability)
        => _capabilities.TryGetValue(name, out capability!);

    public bool TryGetResource(string resourceType, out AiResourceDescriptor resource)
        => _resources.TryGetValue(resourceType, out resource!);

    /// <summary>
    /// Builds the registry from candidate types (only the annotated ones are taken): request types, and entity types
    /// carrying <see cref="AiIndexedAttribute"/> or <see cref="AiQueryableAttribute"/>, which must be among
    /// <paramref name="entityTypes"/> (the types the registered <see cref="Data.IAiEntitySource"/> reads; null when none
    /// is registered).
    /// </summary>
    /// <exception cref="InvalidOperationException">A declaration breaks one of the rules above.</exception>
    public static AiCapabilityRegistry Build(
        IEnumerable<Type> candidates,
        ModulusAiConnectorOptions options,
        IReadOnlyCollection<Type>? entityTypes = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);

        var capabilities = new Dictionary<string, AiCapabilityDescriptor>(StringComparer.Ordinal);
        var resources = new Dictionary<string, AiResourceDescriptor>(StringComparer.Ordinal);
        var queryables = new List<(Type Entity, AiQueryableAttribute Attribute)>();
        var indexedEntities = new List<(Type Entity, AiIndexedAttribute Attribute)>();

        foreach (var type in candidates.Concat(entityTypes ?? []).Distinct())
        {
            var queryable = type.GetCustomAttribute<AiQueryableAttribute>();
            var indexedEntity = type.GetCustomAttribute<AiIndexedAttribute>();
            if (queryable is not null || indexedEntity is not null)
            {
                Check(entityTypes is not null,
                    $"'{type.FullName}' carries [AiQueryable] or [AiIndexed], but no AI entity source is registered " +
                    "(call UseEntityFrameworkCore() on the connector builder).");
                Check(entityTypes!.Contains(type),
                    $"'{type.FullName}' carries [AiQueryable] or [AiIndexed], but no registered data context maps it.");
                if (queryable is not null)
                    queryables.Add((type, queryable));
                if (indexedEntity is not null)
                    indexedEntities.Add((type, indexedEntity));
            }

            var capability = type.GetCustomAttribute<AiCapabilityAttribute>();
            var resource = type.GetCustomAttribute<AiResourceAttribute>();
            if (capability is null && resource is null)
                continue;

            if (IsCommand(type))
                throw new InvalidOperationException(
                    $"'{type.FullName}' is a command: only queries can be AI capabilities (the AI platform is read-only).");
            var responseType = QueryResponseType(type)
                ?? throw new InvalidOperationException(
                    $"'{type.FullName}' carries an AI attribute but is not an IQuery<T>.");
            var permission = type.GetCustomAttribute<RequirePermissionAttribute>()?.Permission;

            if (capability is not null)
            {
                Check(CapabilityName().IsMatch(capability.Name),
                    $"AI capability name '{capability.Name}' on '{type.FullName}' must look like 'App.Module.Entity.Verb'.");
                CheckDescription(capability.Description, capability.Name, options);
                Check(!capabilities.ContainsKey(capability.Name),
                    $"AI capability '{capability.Name}' is declared twice ('{type.FullName}').");
                if (capability.ResourceType is not null)
                    Check(ResourceTypeName().IsMatch(capability.ResourceType),
                        $"AI resource type '{capability.ResourceType}' on '{type.FullName}' is malformed.");

                capabilities[capability.Name] = new AiCapabilityDescriptor(
                    capability.Name,
                    capability.Description,
                    type,
                    responseType,
                    AiFieldCatalog.ItemType(responseType),
                    permission,
                    capability.ResourceType,
                    InputSchema(type));
            }

            if (resource is not null)
            {
                Check(ResourceTypeName().IsMatch(resource.ResourceType),
                    $"AI resource type '{resource.ResourceType}' on '{type.FullName}' must look like 'Module.Entity'.");
                CheckDescription(resource.Description, resource.ResourceType, options);
                Check(!resources.ContainsKey(resource.ResourceType),
                    $"AI resource type '{resource.ResourceType}' has two lookups ('{type.FullName}').");

                var constructors = type.GetConstructors();
                var parameters = constructors.Length == 1 ? constructors[0].GetParameters() : [];
                Check(parameters.Length == 1 && IdTypes.Contains(parameters[0].ParameterType),
                    $"AI resource lookup '{type.FullName}' needs one public constructor taking the id (Guid, string, int or long).");

                var itemType = AiFieldCatalog.ItemType(responseType);
                if (resource.TitleField is not null)
                    Check(AiFieldCatalog.For(itemType).Any(f => f.Property?.Name == resource.TitleField),
                        $"AI resource type '{resource.ResourceType}' names title field '{resource.TitleField}', which '{itemType.Name}' does not have.");

                var batch = resource.BatchLookup is { } batchType
                    ? BatchOf(batchType, resource.ResourceType, parameters.Length == 1 ? parameters[0].ParameterType : typeof(Guid), itemType)
                    : null;

                resources[resource.ResourceType] = new AiResourceDescriptor(
                    resource.ResourceType,
                    resource.Description,
                    type,
                    responseType,
                    itemType,
                    constructors[0],
                    parameters[0].ParameterType,
                    permission,
                    resource.DeepLink,
                    resource.TitleField,
                    batch);
            }
        }

        foreach (var capability in capabilities.Values.Where(c => c.ResourceType is not null))
            Check(resources.ContainsKey(capability.ResourceType!),
                $"AI capability '{capability.Name}' returns '{capability.ResourceType}' records, but no query declares " +
                $"[AiResource(\"{capability.ResourceType}\", ...)] as their lookup.");

        foreach (var (entity, attribute) in queryables)
            AddQueryable(capabilities, resources, entity, attribute, options);

        var indexed = new Dictionary<string, AiIndexedDescriptor>(StringComparer.Ordinal);
        foreach (var (entity, attribute) in indexedEntities)
        {
            Check(resources.TryGetValue(attribute.ResourceType, out var lookup),
                $"'{entity.FullName}' is [AiIndexed(\"{attribute.ResourceType}\")], but no query declares " +
                $"[AiResource(\"{attribute.ResourceType}\", ...)] as its lookup (the index is served by the lookup).");
            Check(!indexed.ContainsKey(attribute.ResourceType),
                $"Two entities are [AiIndexed(\"{attribute.ResourceType}\")] ('{entity.FullName}').");
            var key = entity.GetProperty("Id")?.PropertyType;
            Check(key is null || key == lookup!.IdType,
                $"'{entity.FullName}' has a {key?.Name} key, but the lookup of '{attribute.ResourceType}' takes a {lookup!.IdType.Name}.");
            indexed[attribute.ResourceType] = new AiIndexedDescriptor(attribute.ResourceType, entity, lookup!);
        }

        return new AiCapabilityRegistry(capabilities, resources, indexed);
    }

    private static void AddQueryable(
        Dictionary<string, AiCapabilityDescriptor> capabilities,
        Dictionary<string, AiResourceDescriptor> resources,
        Type entity,
        AiQueryableAttribute attribute,
        ModulusAiConnectorOptions options)
    {
        Check(ResourceTypeName().IsMatch(attribute.ResourceType) && attribute.ResourceType.Contains('.', StringComparison.Ordinal),
            $"[AiQueryable(\"{attribute.ResourceType}\")] on '{entity.FullName}' must name a resource type like 'Module.Entity'.");
        CheckDescription(attribute.Description, attribute.ResourceType, options);
        Check(attribute.Fields.Length > 0,
            $"[AiQueryable] on '{entity.FullName}' lists no Fields; list the properties the platform may filter and read.");

        var catalog = AiFieldCatalog.For(entity);
        var fields = new Dictionary<string, AiField>(StringComparer.Ordinal);
        foreach (var name in attribute.Fields)
        {
            var field = catalog.FirstOrDefault(f => f.Property?.Name == name);
            Check(field is not null, $"[AiQueryable] on '{entity.FullName}' lists '{name}', which is not a public property.");
            Check(AiFieldCatalog.IsScalar(field!.Property!.PropertyType),
                $"[AiQueryable] on '{entity.FullName}' lists '{name}', which is not a scalar property.");
            Check(!field.IsSecret, $"[AiQueryable] on '{entity.FullName}' lists '{name}', which is [SecretData].");
            Check(fields.TryAdd(field.Name, field), $"[AiQueryable] on '{entity.FullName}' lists '{name}' twice.");
        }

        var key = catalog.FirstOrDefault(f => f.Property?.Name == "Id" && !f.IsSecret);
        var queryable = new AiQueryableDescriptor(attribute.ResourceType, entity, attribute.Permission, fields, key);
        List<AiField> output = key is null || fields.ContainsKey(key.Name) ? [.. fields.Values] : [key, .. fields.Values];
        var lookup = resources.ContainsKey(attribute.ResourceType) && key is not null ? attribute.ResourceType : null;

        var search = attribute.ResourceType + ".Search";
        var calculate = attribute.ResourceType + ".Calculate";
        foreach (var name in new[] { search, calculate })
        {
            Check(CapabilityName().IsMatch(name), $"Generated AI capability name '{name}' is malformed.");
            Check(!capabilities.ContainsKey(name), $"AI capability '{name}' is declared twice ('{entity.FullName}').");
        }

        capabilities[search] = new AiCapabilityDescriptor(
            search,
            $"Searches: {attribute.Description} Filters, sorts and returns the listed fields only.",
            entity,
            typeof(object),
            entity,
            attribute.Permission,
            lookup,
            AiQuerySchemas.Search(queryable, options),
            queryable,
            AiGeneratedCapability.Search,
            output);

        capabilities[calculate] = new AiCapabilityDescriptor(
            calculate,
            $"Counts, sums, averages, or finds the minimum or maximum, optionally grouped by one field: {attribute.Description} Use it for figures.",
            entity,
            typeof(object),
            entity,
            attribute.Permission,
            null,
            AiQuerySchemas.Calculate(queryable, options),
            queryable,
            AiGeneratedCapability.Calculate,
            [new AiField(AiQuerySchemas.GroupField, null, "string", null, false), new AiField(AiFieldCatalog.ValueField, null, "number", null, false)]);
    }

    private static void CheckDescription(string description, string owner, ModulusAiConnectorOptions options)
        => Check(description.Length <= options.MaxDescriptionLength,
            $"The AI description of '{owner}' is {description.Length} characters; the limit is {options.MaxDescriptionLength}.");

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static bool IsCommand(Type type)
        => type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));

    private static Type? QueryResponseType(Type type)
        => type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>))
            ?.GetGenericArguments()[0];

    private static JsonNode InputSchema(Type queryType)
        => ConnectorJson.Arguments.GetJsonSchemaAsNode(queryType, new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = static (context, schema) =>
            {
                var description = (context.PropertyInfo?.AttributeProvider ?? context.TypeInfo.Type)
                    .GetCustomAttributes(typeof(DescriptionAttribute), inherit: false)
                    .OfType<DescriptionAttribute>()
                    .FirstOrDefault()?.Description;
                if (description is not null && schema is JsonObject node)
                    node.Insert(0, "description", description);
                return schema;
            },
        });

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*){2,}$")]
    private static partial Regex CapabilityName();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)*$")]
    private static partial Regex ResourceTypeName();
}
