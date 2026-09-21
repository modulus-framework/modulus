using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Modulus.UI;

/// <summary>One <c>&lt;option&gt;</c> of an <c>m-select</c>.</summary>
/// <param name="Value">Posted value.</param>
/// <param name="Text">Visible text.</param>
/// <param name="Selected">Marked selected.</param>
public sealed record SelectOption(string Value, string Text, bool Selected);

/// <summary>View model for the <c>Input</c> and <c>Select</c> components (a labelled form field).</summary>
/// <param name="Id">Element id (sanitised from the field path).</param>
/// <param name="Name">Posted name (full field path, so it model-binds).</param>
/// <param name="Label">Visible label.</param>
/// <param name="Type">Input type (<c>text</c>, <c>email</c>, <c>date</c>, <c>number</c>, <c>textarea</c>, <c>checkbox</c>, ...).</param>
/// <param name="Value">Current value: the posted value on a re-render, else the model's.</param>
/// <param name="Required">Show the required marker and the <c>required</c> attribute.</param>
/// <param name="Errors">Validation messages for this field.</param>
public sealed record FieldModel(
    string Id,
    string Name,
    string Label,
    string Type,
    string Value,
    bool Required,
    IReadOnlyList<string> Errors)
{
    /// <summary>Placeholder text (for a select: the empty first option).</summary>
    public string? Placeholder { get; init; }

    /// <summary>Muted help text under the control.</summary>
    public string? Hint { get; init; }

    /// <summary>Render disabled.</summary>
    public bool Disabled { get; init; }

    /// <summary>Render read-only.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>Browser autofill hint.</summary>
    public string? Autocomplete { get; init; }

    /// <summary>Numeric step (e.g. <c>0.01</c> for money).</summary>
    public string? Step { get; init; }

    /// <summary>Minimum (number/date).</summary>
    public string? Min { get; init; }

    /// <summary>Maximum (number/date).</summary>
    public string? Max { get; init; }

    /// <summary>Rows of a <c>textarea</c>.</summary>
    public int? Rows { get; init; }

    /// <summary>Options of a select.</summary>
    public IReadOnlyList<SelectOption> Options { get; init; } = [];

    /// <summary>File types a file input offers (<c>accept</c>: <c>.pdf,image/*</c>).</summary>
    public string? Accept { get; init; }

    /// <summary>A file input may pick several files.</summary>
    public bool Multiple { get; init; }

    /// <summary>Checkbox state ("true" — also the "true,false" MVC posts for a ticked box).</summary>
    public bool Checked => Value.StartsWith("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the field has validation errors.</summary>
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// Shared plumbing for <c>m-input</c> / <c>m-select</c>: resolves the posted name and id from the
/// <c>for</c> expression, takes the label and required flag from model metadata, prefers the value the
/// user just posted (so a 422 re-render keeps their input), and gathers the field's validation errors.
/// The partial is then plain markup.
/// </summary>
public abstract class FieldTagHelperBase : ComponentTagHelper
{
    /// <summary>The model member the field binds to (<c>for="Input.Email"</c>).</summary>
    [HtmlAttributeName("for")]
    public ModelExpression? For { get; set; }

    /// <summary>Label text; defaults to the member's display name.</summary>
    [HtmlAttributeName("label")]
    public string? Label { get; set; }

    /// <summary>Placeholder text.</summary>
    [HtmlAttributeName("placeholder")]
    public string? Placeholder { get; set; }

    /// <summary>Muted help text under the control.</summary>
    [HtmlAttributeName("hint")]
    public string? Hint { get; set; }

    /// <summary>Overrides whether the field is required (default: from the model's validation metadata).</summary>
    [HtmlAttributeName("required")]
    public bool? Required { get; set; }

    /// <summary>Render disabled.</summary>
    [HtmlAttributeName("disabled")]
    public bool Disabled { get; set; }

    /// <summary>Render read-only.</summary>
    [HtmlAttributeName("readonly")]
    public bool ReadOnly { get; set; }

    /// <summary>
    /// Posted name for a field with no model expression (a handler parameter such as
    /// <c>OnPostUpload(IFormFile file)</c>). Null for helpers that always bind through <c>for</c>.
    /// </summary>
    protected virtual string? ExplicitName => null;

    /// <summary>Builds the shared part of the field's view model.</summary>
    protected FieldModel Build(string type)
    {
        var name = For is not null
            ? ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(For.Name)
            : ExplicitName
              ?? throw new InvalidOperationException($"<m-{Component.ToLowerInvariant()}> requires a for=\"...\" model expression.");
        var id = TagBuilder.CreateSanitizedId(name, "_");
        var isCheckbox = string.Equals(type, "checkbox", StringComparison.OrdinalIgnoreCase);

        ViewContext.ModelState.TryGetValue(name, out var entry);
        if (entry is null && For is not null && string.Equals(type, "file", StringComparison.OrdinalIgnoreCase))
        {
            // With no file part posted at all, the binder skips the prefixed model and validation reports
            // the missing file under the bare property name, so look there too.
            ViewContext.ModelState.TryGetValue(For.Metadata.PropertyName ?? string.Empty, out entry);
        }

        var value = string.Equals(type, "password", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "file", StringComparison.OrdinalIgnoreCase)
            ? string.Empty // never echo a password back into the page (a file input cannot hold a value)
            : entry?.AttemptedValue ?? Format(For?.Model, type);

        var errors = ErrorsOf(entry);

        // A non-nullable bool is "required" to MVC, but demanding a ticked box is never what a form wants.
        var required = Required ?? (!isCheckbox && For is not null && For.Metadata.IsRequired);
        var label = Label ?? For?.Metadata.DisplayName ?? For?.Metadata.PropertyName ?? name;

        return new FieldModel(id, name, label, type, value, required, errors)
        {
            Placeholder = Placeholder,
            Hint = Hint,
            Disabled = Disabled,
            ReadOnly = ReadOnly,
        };
    }

    /// <summary>The validation messages recorded for a field's model-state entry.</summary>
    internal static IReadOnlyList<string> ErrorsOf(ModelStateEntry? entry)
        => entry is null
            ? []
            : entry.Errors
                .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "The value is invalid." : e.ErrorMessage)
                .ToList();

    /// <summary>Formats a model value the way an <c>&lt;input type="..."&gt;</c> expects it (invariant culture).</summary>
    internal static string Format(object? model, string type) => model switch
    {
        null => string.Empty,
        DateTime dt when type == "date" => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dt when type == "datetime-local" => dt.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
        DateTimeOffset dto when type == "date" => dto.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset dto when type == "datetime-local" => dto.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString("HH:mm", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => model.ToString() ?? string.Empty,
    };
}

/// <summary>
/// <c>&lt;m-input for="Input.Email" type="email" /&gt;</c> — a labelled field: label (with required marker),
/// control, validation errors and hint. <c>type</c> covers text, email, password, number, date,
/// datetime-local, textarea and checkbox; use <c>step="0.01"</c> for money and <c>min</c>/<c>max</c> for
/// ranges. Markup lives in the overridable <c>Input/Default</c> view.
/// </summary>
[HtmlTargetElement("m-input")]
public sealed class ModulusInputTagHelper : FieldTagHelperBase
{
    /// <summary>Input type. Default <c>text</c>; <c>textarea</c> renders a textarea.</summary>
    [HtmlAttributeName("type")]
    public string Type { get; set; } = "text";

    /// <summary>Browser autofill hint (<c>email</c>, <c>new-password</c>, ...).</summary>
    [HtmlAttributeName("autocomplete")]
    public string? Autocomplete { get; set; }

    /// <summary>Numeric step.</summary>
    [HtmlAttributeName("step")]
    public string? Step { get; set; }

    /// <summary>Minimum (number/date).</summary>
    [HtmlAttributeName("min")]
    public string? Min { get; set; }

    /// <summary>Maximum (number/date).</summary>
    [HtmlAttributeName("max")]
    public string? Max { get; set; }

    /// <summary>Rows of a textarea.</summary>
    [HtmlAttributeName("rows")]
    public int? Rows { get; set; }

    /// <inheritdoc />
    protected override string Component => "Input";

    /// <inheritdoc />
    public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        => RenderAsync(output, Build(Type) with
        {
            Autocomplete = Autocomplete,
            Step = Step,
            Min = Min,
            Max = Max,
            Rows = Rows,
        });
}

/// <summary>
/// <c>&lt;m-select for="Input.Status" items="Model.Statuses" placeholder="Choose…" /&gt;</c> — a labelled
/// dropdown. The current value (or the posted one on a re-render) selects the matching option; without a
/// value the items' own <c>Selected</c> flags apply. Markup lives in the overridable <c>Select/Default</c> view.
/// </summary>
[HtmlTargetElement("m-select")]
public sealed class ModulusSelectTagHelper : FieldTagHelperBase
{
    /// <summary>The options.</summary>
    [HtmlAttributeName("items")]
    public IEnumerable<SelectListItem>? Items { get; set; }

    /// <inheritdoc />
    protected override string Component => "Select";

    /// <inheritdoc />
    public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var field = Build("select");
        var options = (Items ?? [])
            .Select(i =>
            {
                var val = i.Value ?? i.Text ?? string.Empty;
                var selected = field.Value.Length > 0
                    ? string.Equals(val, field.Value, StringComparison.Ordinal)
                    : i.Selected;
                return new SelectOption(val, i.Text ?? val, selected);
            })
            .ToList();

        return RenderAsync(output, field with { Options = options });
    }
}

/// <summary>
/// <c>&lt;m-file name="file" accept=".pdf,image/*" /&gt;</c> — a labelled file picker: label, control,
/// validation errors and hint. Bind it either with <c>for="Input.Attachment"</c> (an <c>IFormFile</c> member)
/// or, for a handler parameter such as <c>OnPostUpload(IFormFile file)</c>, with <c>name="file"</c>; errors
/// are read from the model state under that name. Put it inside <c>&lt;m-form multipart="true"&gt;</c> so the
/// form posts as <c>multipart/form-data</c> (plain and htmx). A file input can never be pre-filled, so a 422
/// re-render asks for the file again. Markup lives in the overridable <c>File/Default</c> view.
/// </summary>
[HtmlTargetElement("m-file")]
public sealed class ModulusFileTagHelper : FieldTagHelperBase
{
    /// <summary>Posted name when there is no <c>for</c> expression.</summary>
    [HtmlAttributeName("name")]
    public string? Name { get; set; }

    /// <summary>Accepted file types (<c>.pdf,image/*</c>).</summary>
    [HtmlAttributeName("accept")]
    public string? Accept { get; set; }

    /// <summary>Allow picking several files.</summary>
    [HtmlAttributeName("multiple")]
    public bool Multiple { get; set; }

    /// <inheritdoc />
    protected override string Component => "File";

    /// <inheritdoc />
    protected override string? ExplicitName => Name;

    /// <inheritdoc />
    public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        => RenderAsync(output, Build("file") with { Accept = Accept, Multiple = Multiple });
}
