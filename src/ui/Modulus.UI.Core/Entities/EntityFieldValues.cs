using Microsoft.AspNetCore.Mvc.ModelBinding;
using Modulus.Core.Abstractions;

namespace Modulus.UI;

/// <summary>
/// Server side of <c>m-fields</c>: validates and reads the posted bag of extension-field values. Only the fields
/// the current user may see are looked at, so a hand-crafted post cannot set (or fail validation on) a field
/// the form never offered.
/// </summary>
public static class EntityFieldValues
{
    /// <summary>
    /// Runs each visible field's rules against the posted <paramref name="values"/> and files every failure in
    /// <paramref name="modelState"/> under <c>{prefix}[{field}]</c>, where <c>m-fields</c> reads it back on the
    /// 422 re-render. Returns true when every field passed.
    /// </summary>
    /// <param name="registry">The registry holding the contributions.</param>
    /// <param name="entity">Registry key of the entity (<c>Catalog.Product</c>).</param>
    /// <param name="user">The current user (fields needing a permission they lack are skipped).</param>
    /// <param name="values">The posted bag (<c>Input.Extra</c>); null counts as empty.</param>
    /// <param name="modelState">The page's model state.</param>
    /// <param name="prefix">Full path of the bag as posted (<c>Input.Extra</c>).</param>
    public static bool ValidateEntityFields(
        this IEntityUiRegistry registry,
        string entity,
        ICurrentUser user,
        IReadOnlyDictionary<string, string?>? values,
        ModelStateDictionary modelState,
        string prefix)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(modelState);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var valid = true;
        foreach (var field in registry.GetVisibleFields(entity, user))
        {
            var errors = field.Validate(Lookup(values, field.Name));
            foreach (var error in errors)
            {
                modelState.AddModelError($"{prefix}[{field.Name}]", error);
                valid = false;
            }
        }

        return valid;
    }

    /// <summary>
    /// The typed values of the visible fields, keyed by field name (null when nothing was entered). Call it after
    /// <see cref="ValidateEntityFields"/> succeeded; a value that does not convert is left out.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> ReadEntityFields(
        this IEntityUiRegistry registry,
        string entity,
        ICurrentUser user,
        IReadOnlyDictionary<string, string?>? values)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in registry.GetVisibleFields(entity, user))
        {
            if (field.TryConvert(Lookup(values, field.Name), out var value))
            {
                result[field.Name] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// The visible fields' values as canonical invariant text, ready for
    /// <c>entity.SetExtraProperties(...)</c> (Modulus.Core's <c>IHasExtraProperties</c>): a field left empty maps to
    /// null, which removes the stored key, and a field the user may not see is absent, so their save leaves it
    /// untouched. A value that does not convert is left out (validate first with <see cref="ValidateEntityFields"/>).
    /// </summary>
    public static IReadOnlyDictionary<string, string?> ReadEntityFieldText(
        this IEntityUiRegistry registry,
        string entity,
        ICurrentUser user,
        IReadOnlyDictionary<string, string?>? values)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in registry.GetVisibleFields(entity, user))
        {
            if (field.TryConvert(Lookup(values, field.Name), out var value))
            {
                result[field.Name] = field.ToText(value);
            }
        }

        return result;
    }

    private static string? Lookup(IReadOnlyDictionary<string, string?>? values, string name)
    {
        if (values is null)
        {
            return null;
        }

        if (values.TryGetValue(name, out var exact))
        {
            return exact;
        }

        // Field names are case-insensitive, but the binder's dictionary may use the default comparer.
        return values.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
    }
}
