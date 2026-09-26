using Modulus.Core.Abstractions;

namespace Modulus.UI;

/// <summary>
/// One extension field in an <see cref="EntityUiSchema"/>, expressed as a form can render it: the
/// input type is the HTML type (<c>text</c>, <c>number</c>, <c>date</c>, <c>checkbox</c>, ...),
/// <see cref="Required"/> mirrors the field's <c>[Required]</c> validator. Values travel as
/// invariant text, exactly as the in-process <c>m-fields</c> form posts them.
/// </summary>
public sealed record EntityUiSchemaField(
    string Name,
    string Label,
    string InputType,
    bool Required,
    string? Tab,
    string? Hint,
    string? Placeholder,
    string? Step);

/// <summary>
/// The extension fields a caller may see on an entity's form, as an API answer — what the
/// <c>GET .../ui-schema</c> endpoint returns (or null where the entity has none). The server has
/// already filtered the fields by permission, so the schema never names a field the caller must
/// not see. A split (webapp+api) web host renders its forms from this, because the registry that
/// holds the contributions lives in the API process.
/// </summary>
public sealed record EntityUiSchema(string Entity, IReadOnlyList<EntityUiSchemaField> Fields);

/// <summary>Builds an <see cref="EntityUiSchema"/> from the registry's visible fields.</summary>
public static class EntityUiSchemaExtensions
{
    /// <summary>
    /// What the entity's form offers <paramref name="user"/>: only fields the user has the permission
    /// to see (the same filter <c>m-fields</c> applies in-process), in registration order.
    /// </summary>
    public static EntityUiSchema ToUiSchema(this IEntityUiRegistry registry, string entity, ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(user);

        var fields = registry.GetVisibleFields(entity, user)
            .Select(f => new EntityUiSchemaField(
                f.Name, f.Label, f.InputType, f.IsRequired, f.Tab, f.Hint, f.Placeholder, f.Step))
            .ToList();

        return new EntityUiSchema(entity, fields);
    }
}
