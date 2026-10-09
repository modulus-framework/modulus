namespace Modulus.AI.Connector.Capabilities;

using System.Collections;
using System.Globalization;
using System.Text.Json;
using Modulus.AI.Connector.Contract;
using Modulus.Authorization.Fields;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Compliance;
using Modulus.Core.Abstractions.DataProtection;
using Modulus.Core.Abstractions.Entities;

/// <summary>
/// Turns a query result into wire records for the asserted user: secret fields are dropped, fields the user may not
/// read (field security, <see cref="IFieldAuthorizer"/>) are dropped <b>before</b> serialization, at every nesting
/// level, and each record gets its reference and deep link. Scoped: the field masks are the request's.
/// </summary>
internal sealed class AiResultProjector
{
    private const int MaxDepth = 8;

    private readonly IFieldAuthorizer _fields;
    private readonly IFieldSecurityRegistry _profiles;
    private readonly ICurrentUser _currentUser;
    private readonly ModulusAiConnectorOptions _options;

    public AiResultProjector(IServiceProvider services, ICurrentUser currentUser, IOptions<ModulusAiConnectorOptions> options)
    {
        _currentUser = currentUser;
        _profiles = services.GetService<IFieldSecurityRegistry>() ?? EmptyFieldSecurityRegistry.Instance;
        // Without registered field security, classified fields still fail closed: an empty registry opens nothing
        // above Public.
        _fields = services.GetService<IFieldAuthorizer>() ?? new FieldAuthorizer(currentUser, EmptyFieldSecurityRegistry.Instance);
        _options = options.Value;
    }

    /// <summary>Whether the user may see field <paramref name="field"/>.</summary>
    public bool CanRead(AiField field)
        => !field.IsSecret
           && (field.Property is null
               || (_fields.MaskFor(field.Property.DeclaringType!).CanRead(field.Property.Name) && MayReadPersonal(field.Property)));

    // Opt-in (MaskPersonalInformation): personal-information fields need the Restricted clearance of the type's field-security profile.
    private bool MayReadPersonal(System.Reflection.PropertyInfo property)
    {
        if (!_options.MaskPersonalInformation || !IsPersonal(property))
            return true;

        var profile = _profiles.Find(property.DeclaringType!) ?? FieldSecurityProfile.Empty;
        return profile.ReadRequirement(property.Name, FieldClassification.Restricted).IsSatisfiedBy(_currentUser.HasPermission);
    }

    private static bool IsPersonal(System.Reflection.PropertyInfo property)
        => property.IsDefined(typeof(PersonalInformationAttribute), inherit: true)
           || property.IsDefined(typeof(ProtectedPersonalDataAttribute), inherit: true);

    /// <summary>The records of <paramref name="response"/>, at most <paramref name="max"/>.</summary>
    public CapabilityResult Project(object? response, Type itemType, AiResourceDescriptor? resource, int max)
        => Project(AiFieldCatalog.Items(response), AiFieldCatalog.For(itemType), resource, max);

    /// <summary>The records <paramref name="items"/>, with <paramref name="fields"/> only, at most <paramref name="max"/>.</summary>
    public CapabilityResult Project(IEnumerable<object?> items, IReadOnlyList<AiField> fields, AiResourceDescriptor? resource, int max)
    {
        var records = new List<AppResource>();
        var truncated = false;
        foreach (var item in items)
        {
            if (records.Count == max)
            {
                truncated = true;
                break;
            }

            if (item is not null)
                records.Add(ProjectOne(item, fields, resource));
        }

        return new CapabilityResult(records, truncated);
    }

    /// <summary>One record of a resource type.</summary>
    public AppResource ProjectOne(object item, AiResourceDescriptor resource)
        => ProjectOne(item, AiFieldCatalog.For(resource.ItemType), resource);

    /// <summary>One record of a resource type for the index, with who may see it.</summary>
    public IndexedResource Index(object item, string id, AiResourceDescriptor resource, ResourceAccess access)
    {
        var record = ProjectOne(item, resource);
        return new IndexedResource(record.Reference ?? new ResourceReference(resource.ResourceType, id), record.Fields, record.DeepLink, access);
    }

    private AppResource ProjectOne(object item, IReadOnlyList<AiField> fields, AiResourceDescriptor? resource)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (!CanRead(field))
                continue;
            var value = field.Property is null ? item : field.Property.GetValue(item);
            values[field.Name] = JsonSerializer.SerializeToElement(ToWire(value, 0), ConnectorJson.Options);
        }

        if (resource is null || IdOf(item) is not { } id)
            return new AppResource(null, values, null);

        return new AppResource(new ResourceReference(resource.ResourceType, id), values, DeepLink(resource, id));
    }

    // Nested objects go through the same rules as the record itself, so a secret or masked field cannot leak
    // through a child object or a list of them.
    private object? ToWire(object? value, int depth)
    {
        if (value is null || AiFieldCatalog.IsScalar(value.GetType()))
            return value;
        if (depth >= MaxDepth)
            return null;

        if (value is IDictionary dictionary)
        {
            var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in dictionary)
                copy[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty] = ToWire(entry.Value, depth + 1);
            return copy;
        }

        if (value is IEnumerable sequence)
            return sequence.Cast<object?>().Select(v => ToWire(v, depth + 1)).ToList();

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in AiFieldCatalog.For(value.GetType()))
        {
            if (CanRead(field))
                result[field.Name] = ToWire(field.Property!.GetValue(value), depth + 1);
        }

        return result;
    }

    /// <summary>The record's <c>Id</c> property as text, or null when it has none.</summary>
    public static string? IdOf(object item)
        => item.GetType().GetProperty("Id")?.GetValue(item) switch
        {
            null => null,
            Guid guid => guid.ToString("D"),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            var other => other.ToString(),
        };

    private string? DeepLink(AiResourceDescriptor resource, string id)
    {
        if (resource.DeepLink is null)
            return null;
        var link = resource.DeepLink.Replace("{id}", Uri.EscapeDataString(id), StringComparison.Ordinal);
        if (Uri.IsWellFormedUriString(link, UriKind.Absolute) || string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
            return link;
        return _options.PublicBaseUrl.TrimEnd('/') + "/" + link.TrimStart('/');
    }

    private sealed class EmptyFieldSecurityRegistry : IFieldSecurityRegistry
    {
        public static readonly EmptyFieldSecurityRegistry Instance = new();

        public FieldSecurityProfile? Find(Type resourceType) => null;
    }
}
