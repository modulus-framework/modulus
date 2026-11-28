namespace Modulus.Cli.Services;

/// <summary>
/// A property of a CRUD entity, as its generated DTOs, commands and endpoints use it.
/// <list type="bullet">
/// <item><c>Type</c> is the declared type; <c>Bare</c> is the same without <c>?</c>.</item>
/// <item><c>Initial</c> is the default of a DTO or command property, so a create that leaves it out is valid.</item>
/// <item><c>Fallback</c> is the create value when a form left the field empty (<c>string.Empty</c>, <c>null</c> or <c>default(T)</c>).</item>
/// <item><c>UpdateType</c> is the update parameter's type: nullable, so <c>null</c> leaves the stored value alone.</item>
/// <item><c>Unwrap</c> is what an update adds to the parameter when it is assigned to the entity (<c>.Value</c> for a value type).</item>
/// <item><c>Parser</c> is how a form value is read: <c>text</c> (a string), <c>flag</c> (a checkbox) or <c>parse</c> (a typed value).</item>
/// <item><c>Camel</c> is the field's name in camel case.</item>
/// </list>
/// </summary>
internal sealed record CrudField(
    string Name,
    string Type,
    string Bare,
    string Initial,
    string Fallback,
    string UpdateType,
    string Unwrap,
    string Parser,
    string Camel,
    string Label,
    string Kind,
    string InputType,
    bool Required);

/// <summary>Reads the properties a generated CRUD carries from the entity class (<see cref="EntityMetadata"/>).</summary>
internal static class CrudFields
{
    // The types the generated DTOs, forms and endpoints can carry. Navigation properties and enums are not included: an enum
    // is not known without compiling the app, and a navigation property is not a value a form can send.
    private static readonly HashSet<string> Scalars = new(StringComparer.Ordinal)
    {
        "string", "bool", "int", "long", "short", "byte", "decimal", "double", "float",
        "Guid", "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly", "TimeSpan",
    };

    /// <summary>Name only: what generate-crud generated before the fields were read, and the fallback when the entity class is not found.</summary>
    public static IReadOnlyList<CrudField> NameOnly { get; } = [Make(new EntityField("Name", "Name", "string", "text", "text", true))];

    /// <summary>The entity's scalar properties, with <c>Name</c> first (the position the generated code always gave it).</summary>
    public static IReadOnlyList<CrudField> Resolve(string moduleDir, string entity)
    {
        var file = EntityMetadata.FindEntityFile(moduleDir, entity);
        if (file is null)
            return NameOnly;

        var fields = EntityMetadata.Parse(File.ReadAllText(file), entity)
            .Where(f => Scalars.Contains(f.Type.TrimEnd('?')))
            .OrderBy(f => f.Name != "Name")
            .Select(Make)
            .ToList();
        return fields.Count == 0 ? NameOnly : fields;
    }

    /// <summary>The generated view of one entity property.</summary>
    public static CrudField Make(EntityField field)
    {
        var declared = field.Type;
        var bare = declared.TrimEnd('?');
        var nullable = declared.EndsWith('?');
        var parser = bare == "string" ? "text" : bare == "bool" ? "flag" : "parse";
        var camel = char.ToLowerInvariant(field.Name[0]) + field.Name[1..];

        string initial, fallback, updateType, unwrap;
        if (nullable)
        {
            initial = fallback = "null";
            updateType = declared;
            unwrap = "";
        }
        else if (bare == "string")
        {
            // A compile-time constant: a command's parameter default cannot be string.Empty (a static field).
            initial = fallback = "\"\"";
            updateType = "string?";
            unwrap = "";
        }
        else
        {
            initial = "default";
            fallback = $"default({bare})";
            updateType = declared + "?";
            unwrap = ".Value";
        }
        return new CrudField(field.Name, declared, bare, initial, fallback, updateType, unwrap, parser, camel,
            field.Label, field.Kind, field.InputType, field.Required);
    }
}
