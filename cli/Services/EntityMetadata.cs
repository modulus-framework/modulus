using System.Text.RegularExpressions;

namespace Modulus.Cli.Services;

/// <summary>One editable property of an entity, as the form templates see it (<c>kind</c> picks the control).</summary>
internal sealed record EntityField(string Name, string Label, string Type, string Kind, string InputType, bool Required);

/// <summary>Reads the editable properties of an entity class from its C# source (no compilation needed).</summary>
internal static partial class EntityMetadata
{
    // public <type> <Name> { get; set; }   (init also counts; private/protected setters and read-only members don't)
    [GeneratedRegex(@"(?:^|[{};])\s*public\s+(?<type>[\w<>,\.\?\[\]\s]+?)\s+(?<name>[A-Za-z_]\w*)\s*\{\s*get;\s*(?:set|init);\s*\}", RegexOptions.Multiline)]
    private static partial Regex PropertyPattern();

    [GeneratedRegex(@"\bclass\s+(?<name>[A-Za-z_]\w*)")]
    private static partial Regex ClassPattern();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex WordBreak();

    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal)
    {
        "Id", "TenantId", "ExtraProperties", "ConcurrencyStamp", "RowVersion",
        "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy", "ModifiedAt", "ModifiedBy", "DeletedAt", "IsDeleted",
    };

    private static readonly string[] Collections = ["List<", "IList<", "IEnumerable<", "ICollection<", "IReadOnly", "HashSet<", "Dictionary<", "IDictionary<", "[]"];

    private static readonly string[] Numbers = ["int", "long", "short", "decimal", "double", "float", "byte"];

    private static readonly string[] LongText = ["Description", "Notes", "Note", "Comment", "Comments", "Remarks", "Summary", "Body", "Address"];

    public static string? FindEntityFile(string moduleDir, string entity)
    {
        if (!Directory.Exists(moduleDir)) return null;
        return Directory.EnumerateFiles(moduleDir, $"{entity}.cs", SearchOption.AllDirectories)
            .FirstOrDefault(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                              && ClassPattern().Matches(File.ReadAllText(f)).Any(m => m.Groups["name"].Value == entity));
    }

    public static IReadOnlyList<EntityField> Parse(string source, string entity)
    {
        var start = ClassPattern().Matches(source).FirstOrDefault(m => m.Groups["name"].Value == entity)
            ?? throw new InvalidOperationException($"No class '{entity}' found in the source.");
        var body = ClassBody(source, start.Index);

        var fields = new List<EntityField>();
        foreach (Match m in PropertyPattern().Matches(body))
        {
            var type = Regex.Replace(m.Groups["type"].Value.Trim(), @"\s+", " ");
            var name = m.Groups["name"].Value;
            if (Skipped.Contains(name) || type.StartsWith("static ") || Collections.Any(type.Contains))
                continue;

            var nullable = type.EndsWith('?');
            var bare = type.TrimEnd('?');
            var (kind, input) = Classify(bare, name);
            // A non-nullable string must be filled in; value types always hold a value, so they aren't marked "required".
            var required = bare == "string" && !nullable;
            fields.Add(new EntityField(name, Label(name), type, kind, input, required));
        }
        return fields;
    }

    /// <summary>The text from the class keyword to its closing brace (balanced; the braces of <c>{ get; set; }</c> pair up).</summary>
    private static string ClassBody(string source, int from)
    {
        var open = source.IndexOf('{', from);
        if (open < 0) return source[from..];
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[from..(i + 1)];
        }
        return source[from..];
    }

    private static (string Kind, string Input) Classify(string type, string name)
    {
        if (type == "bool") return ("checkbox", "checkbox");
        if (Numbers.Contains(type)) return ("number", "number");
        if (type is "DateTime" or "DateTimeOffset") return ("datetime", "datetime-local");
        if (type == "DateOnly") return ("date", "date");
        if (type == "string" && LongText.Any(w => name.EndsWith(w, StringComparison.Ordinal))) return ("textarea", "text");
        if (type == "string" && name.Contains("Email", StringComparison.Ordinal)) return ("text", "email");
        if (type == "string" && name.Contains("Password", StringComparison.Ordinal)) return ("text", "password");
        return ("text", "text");
    }

    private static string Label(string name) => WordBreak().Replace(name, "$1 $2");

    /// <summary>The module (under <c>src/Modules</c>) whose project holds the entity class, optionally narrowed by name.</summary>
    public static (string ModuleDir, string ModuleName, string EntityFile) FindInApp(string solutionDir, string entity, string? module)
    {
        var modulesDir = Path.Combine(solutionDir, "src", "Modules");
        var candidates = Directory.Exists(modulesDir)
            ? Directory.EnumerateDirectories(modulesDir)
                .Where(d => module is null || Path.GetFileName(d).EndsWith("." + module, StringComparison.OrdinalIgnoreCase))
                .Select(d => (Dir: d, File: FindEntityFile(d, entity)))
                .Where(x => x.File is not null)
                .ToList()
            : [];
        if (candidates.Count == 0)
            throw new InvalidOperationException($"No class '{entity}' found under src/Modules" + (module is null ? "." : $" for module '{module}'."));
        if (candidates.Count > 1)
            throw new InvalidOperationException($"'{entity}' exists in several modules ({string.Join(", ", candidates.Select(c => Path.GetFileName(c.Dir)))}). Pass --module.");

        var (dir, file) = candidates[0];
        return (dir, Path.GetFileName(dir).Split('.').Last(), file!);
    }
}
