namespace Modulus.AI.Connector.Capabilities;

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Compliance.Classification;
using Modulus.Core.Abstractions.Compliance;
using Modulus.Core.Abstractions.Entities;

/// <summary>One field of a result type as the platform sees it.</summary>
/// <param name="Name">The wire name (camelCase, or the property's <c>[JsonPropertyName]</c>).</param>
/// <param name="Property">The property; null for the single <c>value</c> field of a scalar result.</param>
/// <param name="Type">A JSON-schema-like type name.</param>
/// <param name="Declared">The strictest classification its attributes declare; null when it has none.</param>
/// <param name="IsSecret">Marked <c>[SecretData]</c>: never listed, never sent.</param>
internal sealed record AiField(string Name, PropertyInfo? Property, string Type, AiDataClass? Declared, bool IsSecret)
{
    /// <summary>The field's platform class, <paramref name="fallback"/> when it declares none.</summary>
    public AiDataClass ClassOr(AiDataClass fallback) => Declared ?? fallback;
}

/// <summary>
/// Reads the fields of a result type once per type: their wire names, types and classifications. Two attribute
/// families classify a field, and the strictest wins: <see cref="ClassifiedAttribute"/> (which also drives field
/// masking) and the <see cref="ModulusTaxonomy"/> attributes (<c>[InternalData]</c>, <c>[ConfidentialData]</c>,
/// <c>[RestrictedData]</c>, <c>[PersonalInformation]</c> and <c>[ProtectedPersonalData]</c>, which map to
/// Restricted, and <c>[SecretData]</c>, which removes the field).
/// </summary>
internal static class AiFieldCatalog
{
    /// <summary>The field name of a scalar result (a count, a total).</summary>
    public const string ValueField = "value";

    private static readonly ConcurrentDictionary<Type, IReadOnlyList<AiField>> Cache = new();

    /// <summary>The fields of one result item of type <paramref name="itemType"/>.</summary>
    public static IReadOnlyList<AiField> For(Type itemType) => Cache.GetOrAdd(itemType, Build);

    /// <summary>The type of one record in a result of type <paramref name="responseType"/>.</summary>
    public static Type ItemType(Type responseType)
    {
        var type = Nullable.GetUnderlyingType(responseType) ?? responseType;
        if (IsScalar(type))
            return type;
        if (EnumerableItemType(type) is { } item)
            return item;
        if (ItemsProperty(type) is { } property && EnumerableItemType(property.PropertyType) is { } paged)
            return paged;
        return type;
    }

    /// <summary>The records of a result: its elements for a list (or a paged list's <c>Items</c>), else the result itself.</summary>
    public static IEnumerable<object?> Items(object? response)
    {
        switch (response)
        {
            case null:
                return [];
            case string:
                return [response];
        }

        var type = response.GetType();
        if (IsScalar(type))
            return [response];
        if (response is IEnumerable sequence && !IsDictionary(type))
            return sequence.Cast<object?>();
        if (ItemsProperty(type)?.GetValue(response) is IEnumerable items)
            return items.Cast<object?>();
        return [response];
    }

    /// <summary>Whether <paramref name="type"/> is sent as one value rather than as an object with fields.</summary>
    public static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly)
            || type == typeof(TimeOnly) || type == typeof(TimeSpan) || type == typeof(Guid);
    }

    private static IReadOnlyList<AiField> Build(Type type)
    {
        if (IsScalar(type))
            return [new AiField(ValueField, null, TypeName(type), null, false)];

        var fields = new List<AiField>();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0 || property.GetMethod is not { IsPublic: true })
                continue;
            if (property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always })
                continue;

            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            var (declared, secret) = Classify(property);
            fields.Add(new AiField(name, property, TypeName(property.PropertyType), declared, secret));
        }

        return fields;
    }

    private static (AiDataClass? Declared, bool Secret) Classify(PropertyInfo property)
    {
        AiDataClass? declared = property.GetCustomAttribute<ClassifiedAttribute>()?.Classification switch
        {
            FieldClassification.Public => AiDataClass.Public,
            FieldClassification.Internal => AiDataClass.Internal,
            FieldClassification.Confidential => AiDataClass.Confidential,
            FieldClassification.Restricted => AiDataClass.Restricted,
            _ => null,
        };

        var secret = false;
        foreach (var attribute in property.GetCustomAttributes<DataClassificationAttribute>())
        {
            var classification = attribute.Classification;
            if (classification == DataClassification.None)
                continue;

            AiDataClass level;
            if (classification == ModulusTaxonomy.Secret)
            {
                secret = true;
                continue;
            }

            if (classification == ModulusTaxonomy.Internal)
                level = AiDataClass.Internal;
            else if (classification == ModulusTaxonomy.Confidential)
                level = AiDataClass.Confidential;
            else
                level = AiDataClass.Restricted; // Restricted, Personal, and any other taxonomy: the strictest class.

            declared = declared is { } current && current > level ? current : level;
        }

        return (declared, secret);
    }

    private static string TypeName(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string) || type.IsEnum || type == typeof(char))
            return "string";
        if (type == typeof(bool))
            return "boolean";
        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
            || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong))
            return "integer";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return "number";
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
            return "date-time";
        if (type == typeof(DateOnly))
            return "date";
        if (type == typeof(TimeOnly))
            return "time";
        if (type == typeof(TimeSpan))
            return "duration";
        if (type == typeof(Guid))
            return "uuid";
        return EnumerableItemType(type) is not null && !IsDictionary(type) ? "array" : "object";
    }

    private static Type? EnumerableItemType(Type type)
    {
        if (type == typeof(string) || IsDictionary(type))
            return null;
        if (type.IsArray)
            return type.GetElementType();
        var enumerable = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? type
            : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerable?.GetGenericArguments()[0];
    }

    private static PropertyInfo? ItemsProperty(Type type)
        => type.GetProperty("Items", BindingFlags.Public | BindingFlags.Instance) is { } property
            && EnumerableItemType(property.PropertyType) is not null
                ? property
                : null;

    private static bool IsDictionary(Type type)
        => typeof(IDictionary).IsAssignableFrom(type)
            || type.GetInterfaces().Append(type).Any(i => i.IsGenericType
                && (i.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                    || i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
}
