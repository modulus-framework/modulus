namespace Modulus.Authorization.Fields;

using System.Collections;

/// <summary>
/// The output safety net of field security (ADR-04). <see cref="EnsureMasked{T}"/> is a last check on a response after redaction, and
/// <see cref="EnsureNotRedacted"/> refuses to save an instance that redaction has reset. Both name fields only, never values, and both
/// fail as server errors, so a leak or a silent reset never reaches the caller or the database.
/// </summary>
public static class FieldOutputGuard
{
    /// <summary>
    /// Refuses a response that still holds a value in a field the current principal may not read. Redaction resets those fields to
    /// their defaults, so a value here means the response was changed after redaction or was never redacted.
    /// </summary>
    public static void EnsureMasked<T>(IFieldAuthorizer authorizer, T output)
    {
        ArgumentNullException.ThrowIfNull(authorizer);
        if (output is null)
            return;

        Walk(authorizer, output, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    /// <summary>
    /// Refuses to save an instance that <see cref="IFieldAuthorizer.Redact{T}"/> has reset: the masked fields hold defaults, so a save
    /// would overwrite the stored values with them. Redaction is for DTOs and projections, never for tracked entities.
    /// </summary>
    public static void EnsureNotRedacted(object entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var cleared = FieldAuthorizer.ClearedFieldsOf(entity);
        if (cleared.Count > 0)
        {
            throw new InvalidOperationException(
                $"{entity.GetType().Name} was redacted and must not be saved: {string.Join(", ", cleared)} would be overwritten with defaults.");
        }
    }

    // Walks the same graph as Redact (nested objects and collection elements, each by its own type's mask), so the check covers what
    // redaction covers.
    private static void Walk(IFieldAuthorizer authorizer, object instance, HashSet<object> visited)
    {
        if (!visited.Add(instance))
            return;

        var type = instance.GetType();
        var mask = authorizer.MaskFor(type);
        foreach (var property in FieldAuthorizer.SettablePropertiesOf(type))
        {
            var value = property.GetValue(instance);
            if (value is null)
                continue;

            if (!mask.CanRead(property.Name))
            {
                if (!Equals(value, FieldAuthorizer.DefaultValue(property.PropertyType)))
                {
                    throw new InvalidOperationException(
                        $"{type.Name}.{property.Name} holds a value the caller may not read: the response was changed after redaction.");
                }

                continue;
            }

            switch (value)
            {
                case IDictionary:
                    break;
                case IEnumerable items when value is not string:
                    foreach (var item in items)
                    {
                        if (item is not null && FieldAuthorizer.IsModel(item.GetType()))
                            Walk(authorizer, item, visited);
                    }

                    break;
                default:
                    if (FieldAuthorizer.IsModel(value.GetType()))
                        Walk(authorizer, value, visited);
                    break;
            }
        }
    }
}
