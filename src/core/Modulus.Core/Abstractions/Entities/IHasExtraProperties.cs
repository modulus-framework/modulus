namespace Modulus.Core.Abstractions.Entities;

/// <summary>
/// Implement on an entity that other modules may extend with their own fields (for example <c>Inventory</c> adding
/// "Reorder level" to <c>Catalog.Product</c> through the UI's <c>ConfigureEntityUi</c>). The extension values live in
/// <see cref="ExtraProperties"/> — a JSON text column that <c>ModuleDbContext</c> maps for every implementing
/// entity, so the owning module needs no schema change per contributed field.
/// <para>
/// Values are stored as <b>invariant-culture text</b> keyed by the field's name (<c>"25.5"</c>, <c>"true"</c>,
/// <c>"2026-09-20"</c>), the same form the UI posts and reads back, so a form round-trips them without conversion
/// rules of its own. Write them with <see cref="ExtraPropertiesExtensions.SetExtraProperties"/>, which merges instead
/// of replacing so fields a user was not allowed to see are never erased by their save.
/// </para>
/// </summary>
public interface IHasExtraProperties
{
    /// <summary>Extension values by field name; never null (a new entity starts with an empty dictionary).</summary>
    Dictionary<string, string?> ExtraProperties { get; set; }
}

/// <summary>Helpers for <see cref="IHasExtraProperties"/>.</summary>
public static class ExtraPropertiesExtensions
{
    /// <summary>
    /// Merges <paramref name="values"/> into <see cref="IHasExtraProperties.ExtraProperties"/>: a non-empty value sets
    /// (or replaces) the key, a null or empty one removes it, and keys not mentioned are left alone. Pass only the
    /// fields the caller was allowed to edit (the UI's <c>ReadEntityFieldText</c> does), so a save cannot wipe values
    /// it never saw.
    /// </summary>
    public static void SetExtraProperties(this IHasExtraProperties entity, IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(values);

        entity.ExtraProperties ??= [];
        foreach (var (name, value) in values)
        {
            if (string.IsNullOrEmpty(value))
            {
                entity.ExtraProperties.Remove(name);
            }
            else
            {
                entity.ExtraProperties[name] = value;
            }
        }
    }

    /// <summary>The stored text of <paramref name="name"/>, or null when the entity has no value for it.</summary>
    public static string? GetExtraProperty(this IHasExtraProperties entity, string name)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return entity.ExtraProperties is { } bag && bag.TryGetValue(name, out var value) ? value : null;
    }
}
