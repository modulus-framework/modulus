using System.Text.Json;

namespace Modulus.Cli.Services;

/// <summary>
/// Generates UI files from templates using the template package resolver.
/// Handles all three engines: MVC, Razor Pages, and Blazor.
/// </summary>
internal class UiFileGenerator
{
    private readonly ITemplatePackageResolver _resolver;
    private readonly ScribanRenderer _renderer = new();

    public UiFileGenerator(ITemplatePackageResolver resolver)
    {
        _resolver = resolver;
    }

    /// <summary>
    /// Generate all UI files for an entity.
    /// </summary>
    /// <param name="module">Module information</param>
    /// <param name="entity">Entity name</param>
    /// <param name="uiEngine">UI engine (mvc, razor-pages, blazor, fluid)</param>
    /// <param name="outputPath">Base output directory</param>
    /// <returns>List of generated file paths</returns>
    public async Task<List<string>> GenerateAsync(
        ModuleModel module,
        string entity,
        string uiEngine,
        string outputPath)
    {
        var generated = new List<string>();

        // Resolve templates package
        var templatesPath = await _resolver.ResolveAsync(UiTemplatePackage.PackageId, UiTemplatePackage.DefaultVersion);
        if (templatesPath == null)
        {
            Ux.Warning("Could not resolve templates package; skipping UI generation");
            return generated;
        }

        try
        {
            // Load ui-kit.json manifest
            var manifestPath = Path.Combine(Path.GetDirectoryName(templatesPath)!, "ui-kit.json");

            if (!File.Exists(manifestPath))
            {
                Ux.Warning("ui-kit.json not found; skipping UI generation");
                return generated;
            }

            var manifestJson = await File.ReadAllTextAsync(manifestPath);
            using var doc = JsonDocument.Parse(manifestJson);
            var root = doc.RootElement;

            // Get CRUD feature definition
            if (!root.TryGetProperty("features", out var features) ||
                !features.TryGetProperty("crud", out var crud) ||
                !crud.TryGetProperty("files", out var filesObj) ||
                !filesObj.TryGetProperty(uiEngine, out var engineFiles))
            {
                Ux.Warning($"No CRUD templates defined for {uiEngine}");
                return generated;
            }

            // Prepare context
            var entityPlural = Pluralize(entity);
            var context = new UiTemplateContext
            {
                Entity = entity,
                EntityPlural = entityPlural,
                EntityLower = CodeGen.ToCamelCase(entity),
                RouteName = entityPlural.ToLowerInvariant(),
                Module = module,
                UiEngine = uiEngine,
                Namespace = $"{module.RootNamespace}.{module.ModuleName}.Presentation",
            };

            // Generate each template file
            foreach (var fileElement in engineFiles.EnumerateArray())
            {
                var entry = ReadFileEntry(fileElement);
                if (string.IsNullOrEmpty(entry.FilePath) || entry.Skip) continue;
                var pattern = entry.FilePath;

                try
                {
                    // Substitute placeholders
                    var outputFile = SubstitutePlaceholders(pattern, entity, entityPlural, module.ModuleName);
                    var fullPath = Path.Combine(outputPath, outputFile);

                    // Determine template file name
                    var templateFileName = entry.Template ?? GetTemplateFileName(pattern, uiEngine);
                    var templatePath = Path.Combine(templatesPath, uiEngine, templateFileName);

                    if (!File.Exists(templatePath))
                    {
                        Ux.Detail($"Template not found: {templatePath}");
                        continue;
                    }

                    // Render template
                    var templateContent = await File.ReadAllTextAsync(templatePath);
                    var rendered = _renderer.Render(templateContent, context);

                    // Write file
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    await File.WriteAllTextAsync(fullPath, rendered);

                    generated.Add(outputFile);
                    Ux.Success($"Generated: {outputFile}");
                }
                catch (Exception ex)
                {
                    Ux.Detail($"Failed to generate {pattern}: {ex.Message}");
                    continue;
                }
            }

            return generated;
        }
        catch (Exception ex)
        {
            Ux.Error($"UI generation failed: {ex.Message}");
            return generated;
        }
    }

    /// <summary>
    /// Reads one manifest entry. A plain string is the legacy form, matched to a template by name. An object
    /// <c>{ "path", "template" }</c> names its template explicitly; a null template means this path makes no file here.
    /// </summary>
    private static (string? FilePath, string? Template, bool Skip) ReadFileEntry(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return LegacyEntry(element.GetString());

        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("path", out var pathElement))
            return (null, null, true);

        var path = pathElement.GetString();
        if (!element.TryGetProperty("template", out var template))
            return LegacyEntry(path);

        return template.ValueKind == JsonValueKind.Null
            ? (path, null, true)
            : (path, template.GetString(), false);
    }

    // A Razor PageModel has no view template. Without this skip, the view template would be rendered into Index.cshtml.cs.
    private static (string? FilePath, string? Template, bool Skip) LegacyEntry(string? path)
        => (path, null, path is not null && (path.EndsWith(".cshtml.cs", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase)));

    private string SubstitutePlaceholders(string pattern, string entity, string plural, string module)
    {
        return pattern
            .Replace("[Module]", module, StringComparison.OrdinalIgnoreCase)
            .Replace("[Entity]", entity, StringComparison.OrdinalIgnoreCase)
            .Replace("[Entities]", plural, StringComparison.OrdinalIgnoreCase)
            .Replace("[entity]", CodeGen.ToCamelCase(entity), StringComparison.OrdinalIgnoreCase)
            .Replace("[entities]", CodeGen.ToCamelCase(plural), StringComparison.OrdinalIgnoreCase);
    }

    private string GetTemplateFileName(string pattern, string uiEngine)
    {
        var ext = GetTemplateExtension(uiEngine);

        if (pattern.Contains("Index", StringComparison.OrdinalIgnoreCase))
            return $"crud-index.{ext}.sbn";

        if (pattern.Contains("Create", StringComparison.OrdinalIgnoreCase))
            return $"crud-create.{ext}.sbn";

        // The shared form is checked before Edit: "Editor" contains "Edit".
        if (pattern.Contains("_Form", StringComparison.OrdinalIgnoreCase) || pattern.Contains("Editor", StringComparison.OrdinalIgnoreCase))
            return $"crud-form.{ext}.sbn";

        if (pattern.Contains("Edit", StringComparison.OrdinalIgnoreCase))
            return $"crud-edit.{ext}.sbn";

        if (pattern.Contains("ApiClient", StringComparison.OrdinalIgnoreCase))
            return "api-client.cs.sbn";

        if (pattern.Contains("Menu", StringComparison.OrdinalIgnoreCase))
            return "menu-contributor.cs.sbn";

        return "unknown.sbn";
    }

    private string GetTemplateExtension(string uiEngine) => uiEngine switch
    {
        "mvc" => "cshtml",
        "razor-pages" => "cshtml",
        "blazor" => "razor",
        "fluid" => "liquid",
        _ => "html"
    };

    private string Pluralize(string singular)
    {
        if (singular.EndsWith("y", StringComparison.OrdinalIgnoreCase))
            return singular[..^1] + "ies";
        if (singular.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            return singular + "es";
        return singular + "s";
    }
}
