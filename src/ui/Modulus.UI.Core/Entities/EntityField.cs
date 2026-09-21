using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Modulus.UI;

/// <summary>
/// A form field one module contributes to another module's entity form (<c>Inventory</c> adds "Reorder level"
/// to <c>Catalog.Product</c>). Registered through <c>services.ConfigureEntityUi(entity, e =&gt; e.Fields.Add(...))</c>
/// and rendered by <c>&lt;m-fields entity="..." for="Input.Extra" /&gt;</c>. The value travels as text (a
/// <c>Dictionary&lt;string, string?&gt;</c> on the page's input model); <see cref="TryConvert"/> and
/// <see cref="Validate"/> turn it into <see cref="ClrType"/> and check it against <see cref="Validators"/>.
/// </summary>
public sealed class EntityField
{
    /// <summary>Builds a field.</summary>
    /// <param name="name">Key of the value in the posted bag; must not contain <c>.</c>, <c>[</c> or <c>]</c>.</param>
    /// <param name="clrType">Value type: string, bool, int, long, decimal, double, DateOnly, DateTime or TimeOnly (or the nullable form).</param>
    /// <param name="label">Visible, already localised label.</param>
    /// <param name="tab">Tab the field belongs to (<c>&lt;m-fields tab="..."&gt;</c> renders one tab's fields).</param>
    /// <param name="order">Sort key (ascending, ties by label).</param>
    /// <param name="validators">Server-side rules; a <see cref="RequiredAttribute"/> also shows the required marker.</param>
    /// <param name="requiredPermission">Hidden (and not validated or read) unless the current user has it.</param>
    /// <param name="hint">Muted help text under the control.</param>
    /// <param name="placeholder">Placeholder text.</param>
    /// <param name="inputType">Overrides the input type derived from <paramref name="clrType"/> (<c>email</c>, <c>textarea</c>, ...).</param>
    public EntityField(
        string name,
        Type clrType,
        string label,
        string? tab = null,
        int order = 100,
        IReadOnlyList<ValidationAttribute>? validators = null,
        string? requiredPermission = null,
        string? hint = null,
        string? placeholder = null,
        string? inputType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(clrType);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (name.IndexOfAny(['.', '[', ']']) >= 0)
        {
            throw new ArgumentException($"Entity field name '{name}' must not contain '.', '[' or ']' (it is a key in the posted bag).", nameof(name));
        }

        var underlying = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (!IsSupported(underlying))
        {
            throw new ArgumentException(
                $"Entity field '{name}' has unsupported type {clrType}; use string, bool, int, long, decimal, double, DateOnly, DateTime or TimeOnly.",
                nameof(clrType));
        }

        Name = name;
        ClrType = clrType;
        Label = label;
        Tab = tab;
        Order = order;
        Validators = validators ?? [];
        RequiredPermission = requiredPermission;
        Hint = hint;
        Placeholder = placeholder;
        InputType = inputType ?? DefaultInputType(underlying);
        Step = underlying == typeof(decimal) || underlying == typeof(double) ? "any" : null;
        UnderlyingType = underlying;
    }

    /// <summary>Key of the value in the posted bag.</summary>
    public string Name { get; }

    /// <summary>Value type.</summary>
    public Type ClrType { get; }

    /// <summary>Visible label.</summary>
    public string Label { get; }

    /// <summary>Tab the field belongs to, if any.</summary>
    public string? Tab { get; }

    /// <summary>Sort key (ascending).</summary>
    public int Order { get; }

    /// <summary>Server-side validation rules.</summary>
    public IReadOnlyList<ValidationAttribute> Validators { get; }

    /// <summary>Permission the current user needs to see (and post) the field; null = everyone.</summary>
    public string? RequiredPermission { get; }

    /// <summary>Muted help text.</summary>
    public string? Hint { get; }

    /// <summary>Placeholder text.</summary>
    public string? Placeholder { get; }

    /// <summary>HTML input type (<c>text</c>, <c>number</c>, <c>date</c>, <c>checkbox</c>, ...).</summary>
    public string InputType { get; }

    /// <summary>Numeric step (<c>any</c> for decimal and double), null otherwise.</summary>
    public string? Step { get; }

    /// <summary>True when a <see cref="RequiredAttribute"/> is present (never for a checkbox).</summary>
    public bool IsRequired => InputType != "checkbox" && Validators.Any(v => v is RequiredAttribute);

    private Type UnderlyingType { get; }

    /// <summary>
    /// Converts posted text to <see cref="ClrType"/> using the invariant culture (the browser's number and date
    /// inputs post invariant text). Empty text is "no value" (null); a checkbox is true when the post contains
    /// <c>true</c>. Returns false when the text is not a valid value.
    /// </summary>
    public bool TryConvert(string? raw, out object? value)
    {
        if (UnderlyingType == typeof(bool))
        {
            value = raw is not null && raw.Contains("true", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            value = null;
            return true;
        }

        var text = raw.Trim();
        if (UnderlyingType == typeof(string))
        {
            value = raw;
            return true;
        }

        if (UnderlyingType == typeof(int)) return Parse<int>(text, out value);
        if (UnderlyingType == typeof(long)) return Parse<long>(text, out value);
        if (UnderlyingType == typeof(decimal)) return Parse<decimal>(text, out value);
        if (UnderlyingType == typeof(double)) return Parse<double>(text, out value);
        if (UnderlyingType == typeof(DateOnly)) return Parse<DateOnly>(text, out value);
        if (UnderlyingType == typeof(DateTime)) return Parse<DateTime>(text, out value);
        return Parse<TimeOnly>(text, out value);
    }

    /// <summary>
    /// The canonical invariant text of a converted value, the form stored in <c>IHasExtraProperties.ExtraProperties</c>
    /// and posted back by the input (<c>true</c>/<c>false</c>, <c>25.5</c>, <c>2026-09-20</c>, <c>2026-09-20T10:30</c>,
    /// <c>08:15</c>; seconds are kept only when non-zero). Null is "no value". Round-trips through <see cref="TryConvert"/>.
    /// </summary>
    public string? ToText(object? value) => value switch
    {
        null => null,
        bool b => b ? "true" : "false",
        string s => s,
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString(dt.Second == 0 && dt.Millisecond == 0 ? "yyyy-MM-ddTHH:mm" : "yyyy-MM-ddTHH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString(t.Second == 0 && t.Millisecond == 0 ? "HH:mm" : "HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>Validation messages for posted text (empty when it is valid): a conversion failure, else the failing <see cref="Validators"/>.</summary>
    public IReadOnlyList<string> Validate(string? raw)
    {
        if (!TryConvert(raw, out var value))
        {
            return [$"The value '{raw}' is not valid for {Label}."];
        }

        var errors = new List<string>();
        var context = new ValidationContext(this) { DisplayName = Label, MemberName = Name };
        foreach (var validator in Validators)
        {
            if (validator.GetValidationResult(value, context) is { } failure)
            {
                errors.Add(failure.ErrorMessage ?? $"The {Label} field is not valid.");
            }
        }

        return errors;
    }

    private static bool Parse<T>(string text, out object? value)
        where T : struct, IParsable<T>
    {
        var ok = T.TryParse(text, CultureInfo.InvariantCulture, out var parsed);
        value = ok ? parsed : null;
        return ok;
    }

    private static bool IsSupported(Type type)
        => type == typeof(string) || type == typeof(bool) || type == typeof(int) || type == typeof(long)
           || type == typeof(decimal) || type == typeof(double)
           || type == typeof(DateOnly) || type == typeof(DateTime) || type == typeof(TimeOnly);

    private static string DefaultInputType(Type type)
    {
        if (type == typeof(bool)) return "checkbox";
        if (type == typeof(int) || type == typeof(long) || type == typeof(decimal) || type == typeof(double)) return "number";
        if (type == typeof(DateOnly)) return "date";
        if (type == typeof(DateTime)) return "datetime-local";
        if (type == typeof(TimeOnly)) return "time";
        return "text";
    }
}
