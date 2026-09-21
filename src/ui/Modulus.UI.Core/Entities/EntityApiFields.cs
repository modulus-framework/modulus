using Modulus.Core.Abstractions;

namespace Modulus.UI;

/// <summary>
/// The API side of extension fields: the same registry and the same per-field permission filter as
/// <c>m-fields</c>, for callers that are not a browser form (a mobile or desktop app, another system). An endpoint
/// must go through these rather than exposing the stored <c>ExtraProperties</c> bag, or a caller could read or set
/// a field the registry hides from them.
/// <para>
/// They differ from <see cref="EntityFieldValues"/> in two ways that matter for an API: a field the caller did not
/// send is left alone on a partial update (a form always posts every field, so an empty one means "clear it"), and a
/// name that is not a visible field is an error (naming a field the caller may not see reads the same as naming one
/// that does not exist, so the response does not reveal it).
/// </para>
/// </summary>
public static class EntityApiFields
{
    /// <summary>
    /// The validation errors for a bag sent by an API caller; empty when it is acceptable. Only the fields the
    /// <paramref name="user"/> may see are considered, and any other name is reported as unknown.
    /// </summary>
    /// <param name="registry">The registry holding the contributions.</param>
    /// <param name="entity">Registry key of the entity (<c>Catalog.Product</c>).</param>
    /// <param name="user">The caller.</param>
    /// <param name="values">The bag as sent (field name to invariant text); null counts as empty.</param>
    /// <param name="partial">
    /// True for an update: fields not sent are not checked (nor changed). False for a create: every visible field is
    /// checked, so a required one that is missing is reported.
    /// </param>
    public static IReadOnlyList<string> ValidateEntityFieldsForApi(
        this IEntityUiRegistry registry,
        string entity,
        ICurrentUser user,
        IReadOnlyDictionary<string, string?>? values,
        bool partial)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var visible = registry.GetVisibleFields(entity, user);
        var errors = new List<string>();

        foreach (var name in values?.Keys ?? Enumerable.Empty<string>())
        {
            if (!visible.Any(f => Same(f.Name, name)))
            {
                errors.Add($"Unknown extension field '{name}'.");
            }
        }

        foreach (var field in visible)
        {
            if (partial && !Has(values, field.Name))
            {
                continue;
            }

            errors.AddRange(field.Validate(Lookup(values, field.Name)).Select(error => $"{field.Name}: {error}"));
        }

        return errors;
    }

    /// <summary>
    /// The values to store, as canonical invariant text for <c>entity.SetExtraProperties(...)</c>: only the visible
    /// fields the caller actually sent, with an empty value mapping to null (which removes the stored key). Fields
    /// not sent, and fields the caller may not see, are absent, so a save leaves them untouched. Validate first with
    /// <see cref="ValidateEntityFieldsForApi"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> ReadSubmittedEntityFieldText(
        this IEntityUiRegistry registry,
        string entity,
        ICurrentUser user,
        IReadOnlyDictionary<string, string?>? values)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in registry.GetVisibleFields(entity, user))
        {
            if (Has(values, field.Name) && field.TryConvert(Lookup(values, field.Name), out var value))
            {
                result[field.Name] = field.ToText(value);
            }
        }

        return result;
    }

    /// <summary>
    /// What to return to the caller from a stored <c>ExtraProperties</c> bag: only the entries of fields the
    /// <paramref name="user"/> may see, keyed by the field's name. A value stored for a field that has since been
    /// removed, or that needs a permission the caller lacks, is not returned.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> VisibleExtraProperties(
        this IEntityUiRegistry registry,
        string entity,
        ICurrentUser user,
        IReadOnlyDictionary<string, string?>? stored)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (stored is null)
        {
            return result;
        }

        foreach (var field in registry.GetVisibleFields(entity, user))
        {
            if (Lookup(stored, field.Name) is { } value)
            {
                result[field.Name] = value;
            }
        }

        return result;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool Has(IReadOnlyDictionary<string, string?>? values, string name)
        => values is not null && values.Keys.Any(k => Same(k, name));

    private static string? Lookup(IReadOnlyDictionary<string, string?>? values, string name)
        => values?.FirstOrDefault(kv => Same(kv.Key, name)).Value;
}
