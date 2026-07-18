namespace Modulus.Authorization.Fields;

using System.Collections.Concurrent;
using System.Reflection;
using Modulus.Core.Abstractions.Entities;

/// <summary>
/// Reads and caches the <see cref="FieldClassification"/> of every public instance
/// property of a type from its <see cref="ClassifiedAttribute"/>s. Reflection runs once
/// per type for the process (the map is immutable and cached), satisfying the blueprint's
/// requirement that per-field checks resolve from cache, not per-row reflection or DB
/// look-ups. Unannotated properties default to <see cref="FieldClassification.Public"/>.
/// </summary>
public static class FieldClassificationMap
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, FieldClassification>> Cache = new();

    /// <summary>The property-name → classification map for <paramref name="type"/> (case-insensitive keys).</summary>
    public static IReadOnlyDictionary<string, FieldClassification> For(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Cache.GetOrAdd(type, Build);
    }

    private static IReadOnlyDictionary<string, FieldClassification> Build(Type type)
    {
        var map = new Dictionary<string, FieldClassification>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0)
                continue;
            map[property.Name] = property.GetCustomAttribute<ClassifiedAttribute>()?.Classification
                ?? FieldClassification.Public;
        }

        return map;
    }
}
