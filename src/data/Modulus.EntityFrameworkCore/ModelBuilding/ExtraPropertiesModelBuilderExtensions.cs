namespace Modulus.EntityFrameworkCore.ModelBuilding;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Modulus.Core.Abstractions.Entities;

/// <summary>
/// Maps <see cref="IHasExtraProperties.ExtraProperties"/> as a JSON text column on every entity that implements
/// the marker. <c>ModuleDbContext</c> calls this from <c>OnModelCreating</c>; call it yourself only from a
/// <c>DbContext</c> that does not derive from it.
/// </summary>
public static class ExtraPropertiesModelBuilderExtensions
{
    private const string PropertyName = nameof(IHasExtraProperties.ExtraProperties);

    /// <summary>
    /// Stores the dictionary as JSON with a value comparer that looks at the content, so changing an entry in place
    /// (<c>entity.ExtraProperties["Bin"] = "A-7"</c>) is detected as a modification and saved. The column is required;
    /// a null dictionary is written as <c>{}</c> and a null or blank column reads back as an empty dictionary.
    /// </summary>
    public static ModelBuilder UseModulusExtraProperties(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var comparer = new ValueComparer<Dictionary<string, string?>>(
            (a, b) => SameContent(a, b),
            d => ContentHash(d),
            d => new Dictionary<string, string?>(d));

        // Only the root of a hierarchy: derived types inherit the property.
        var roots = modelBuilder.Model.GetEntityTypes()
            .Where(e => typeof(IHasExtraProperties).IsAssignableFrom(e.ClrType) && e.BaseType is null)
            .Select(e => e.ClrType)
            .ToList();

        foreach (var clrType in roots)
        {
            modelBuilder.Entity(clrType)
                .Property<Dictionary<string, string?>>(PropertyName)
                .HasConversion(
                    d => Serialize(d),
                    s => Deserialize(s),
                    comparer)
                .IsRequired();
        }

        return modelBuilder;
    }

    private static bool SameContent(Dictionary<string, string?>? a, Dictionary<string, string?>? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null || a.Count != b.Count)
        {
            return false;
        }

        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other) || !string.Equals(value, other, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    // Order-independent, so two dictionaries with the same content hash alike.
    private static int ContentHash(Dictionary<string, string?> values)
        => values.Aggregate(0, (hash, kv) => hash ^ HashCode.Combine(kv.Key, kv.Value));

    private static string Serialize(Dictionary<string, string?>? values)
        => values is { Count: > 0 } ? JsonSerializer.Serialize(values) : "{}";

    private static Dictionary<string, string?> Deserialize(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? [];
}
