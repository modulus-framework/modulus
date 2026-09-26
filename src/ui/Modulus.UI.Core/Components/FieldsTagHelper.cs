using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;

namespace Modulus.UI;

/// <summary>View model for the <c>Fields</c> component.</summary>
/// <param name="Entity">Registry key of the entity the fields were contributed to.</param>
/// <param name="Fields">One rendered <c>Input</c> per visible field, in order.</param>
public sealed record EntityFieldsModel(string Entity, IReadOnlyList<IHtmlContent> Fields);

/// <summary>
/// <c>&lt;m-fields entity="Catalog.Product" for="Input.Extra" /&gt;</c> — renders the fields other modules
/// contributed to an entity (<c>services.ConfigureEntityUi</c>) inside a form. <c>for</c> is the page's bag of
/// extension values, a <c>Dictionary&lt;string, string?&gt;</c> (or <c>&lt;string, object?&gt;</c>) member: each field
/// posts as <c>Input.Extra[Name]</c>, shows its current value from the bag (or the posted one on a 422
/// re-render) and its validation errors. Fields needing a permission the user lacks are left out. <c>tab</c>
/// renders only that tab's fields (omit it to render them all). Every field goes through the overridable
/// <c>Input</c> component, and the wrapper is the overridable <c>Fields/Default</c> view. On post, call
/// <see cref="EntityFieldValues.ValidateEntityFields"/> and <see cref="EntityFieldValues.ReadEntityFields"/>.
/// <para>
/// Over HTTP (the webapp+api split) the registry lives in the API process, so the page passes the schema it
/// fetched from the <c>ui-schema</c> endpoint instead: <c>&lt;m-fields entity="..." schema="@Model.UiSchema"
/// for="Input.Extra" /&gt;</c> renders exactly those fields — the server has already filtered them by permission.
/// </para>
/// </summary>
[HtmlTargetElement("m-fields")]
public sealed class ModulusFieldsTagHelper : ComponentTagHelper
{
    /// <summary>Registry key of the entity (<c>Catalog.Product</c>).</summary>
    [HtmlAttributeName("entity")]
    public string? Entity { get; set; }

    /// <summary>The bag member holding the extension values (<c>for="Input.Extra"</c>).</summary>
    [HtmlAttributeName("for")]
    public ModelExpression? For { get; set; }

    /// <summary>
    /// Render these fields instead of the registry's (<c>schema="@Model.UiSchema"</c>): the schema an API's
    /// <c>ui-schema</c> endpoint returned. When set, no <see cref="IEntityUiRegistry"/> is consulted.
    /// </summary>
    [HtmlAttributeName("schema")]
    public EntityUiSchema? Schema { get; set; }

    /// <summary>Render only this tab's fields; null or empty renders every contributed field.</summary>
    [HtmlAttributeName("tab")]
    public string? Tab { get; set; }

    /// <inheritdoc />
    protected override string Component => "Fields";

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var expression = For ?? throw new InvalidOperationException("<m-fields> requires a for=\"...\" model expression (the extension-values dictionary).");
        var type = expression.Metadata.ModelType;
        if (!typeof(IDictionary<string, string>).IsAssignableFrom(type) && !typeof(IDictionary<string, object>).IsAssignableFrom(type))
        {
            throw new InvalidOperationException(
                $"<m-fields for=\"{expression.Name}\"> must bind a Dictionary<string, string?> (or <string, object?>), but {type} is not one.");
        }

        // A null expression (tab="@Model.Tab") reaches a string attribute as "", which must mean "all", not "no tab".
        static bool InTab(string? fieldTab, string? requested) =>
            string.IsNullOrWhiteSpace(requested) || string.Equals(fieldTab, requested, StringComparison.OrdinalIgnoreCase);

        IEnumerable<FieldShape> fields;
        if (Schema is { } schema)
        {
            fields = schema.Fields
                .Where(f => InTab(f.Tab, Tab))
                .Select(f => new FieldShape(f.Name, f.Label, f.InputType, f.Required, f.Hint, f.Placeholder, f.Step));
        }
        else
        {
            var entity = !string.IsNullOrWhiteSpace(Entity)
                ? Entity
                : throw new InvalidOperationException("<m-fields> requires an entity=\"...\" attribute.");
            var services = ViewContext.HttpContext.RequestServices;
            fields = services.GetRequiredService<IEntityUiRegistry>()
                .GetVisibleFields(entity, services.GetRequiredService<ICurrentUser>())
                .Where(f => InTab(f.Tab, Tab))
                .Select(f => new FieldShape(f.Name, f.Label, f.InputType, f.IsRequired, f.Hint, f.Placeholder, f.Step));
        }

        var prefix = ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(expression.Name);
        var rendered = new List<IHtmlContent>();
        foreach (var field in fields)
        {
            rendered.Add(await ComponentRenderer.RenderAsync(ViewContext, "Input", BuildField(field, prefix, expression.Model)));
        }

        await RenderAsync(output, new EntityFieldsModel(Schema?.Entity ?? Entity!, rendered));
    }

    private FieldModel BuildField(FieldShape field, string prefix, object? bag)
    {
        var name = $"{prefix}[{field.Name}]";
        ViewContext.ModelState.TryGetValue(name, out var entry);

        return new FieldModel(
            TagBuilder.CreateSanitizedId(name, "_"),
            name,
            field.Label,
            field.InputType,
            entry?.AttemptedValue ?? Current(bag, field.Name, field.InputType),
            field.Required,
            FieldTagHelperBase.ErrorsOf(entry))
        {
            Hint = field.Hint,
            Placeholder = field.Placeholder,
            Step = field.Step,
        };
    }

    /// <summary>The field's value in the bag, formatted for its input type (empty when absent).</summary>
    private static string Current(object? bag, string fieldName, string inputType)
    {
        object? value = bag switch
        {
            IDictionary<string, string> strings => strings.TryGetValue(fieldName, out var s) ? s : null,
            IDictionary<string, object> objects => objects.TryGetValue(fieldName, out var o) ? o : null,
            _ => null,
        };

        return value as string ?? FieldTagHelperBase.Format(value, inputType);
    }

    /// <summary>The parts of a field the Input component renders, from either the registry or a schema DTO.</summary>
    private sealed record FieldShape(
        string Name,
        string Label,
        string InputType,
        bool Required,
        string? Hint,
        string? Placeholder,
        string? Step);
}
