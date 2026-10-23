using System.Reflection;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace Modulus.Cli.Services;

/// <summary>
/// Renders embedded Scriban templates for code generation.
/// Templates live under <c>Templates/</c> as <c>.sbn</c> files.
/// </summary>
internal sealed class TemplateEngine
{
    private readonly Assembly _assembly = typeof(TemplateEngine).Assembly;

    /// <summary>
    /// Renders a template by embedded resource path (e.g. <c>"app/Program.cs"</c>), or from disk when given an absolute file path.
    /// </summary>
    public string Render(string templatePath, object model, string? outputPath = null)
    {
        string templateSource;
        if (Path.IsPathRooted(templatePath))
        {
            // A template file from the resolved Modulus.Ui.Templates package.
            if (!File.Exists(templatePath))
                throw new FileNotFoundException($"Template file not found: {templatePath}");
            templateSource = File.ReadAllText(templatePath);
        }
        else
        {
            var resourceName = $"Modulus.Cli.Templates.{templatePath.Replace('/', '.')}.sbn";
            using var stream = _assembly.GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException(
                    $"Embedded template not found: {resourceName}");
            using var reader = new StreamReader(stream);
            templateSource = reader.ReadToEnd();
        }

        var template = Template.Parse(templateSource);
        if (template.HasErrors)
            throw new InvalidOperationException(
                $"Template parse errors in '{templatePath}':\n" +
                string.Join("\n", template.Messages));

        var scriptObject = new ScriptObject();
        foreach (var prop in model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var value = prop.GetValue(model);
            if (value is not null)
                scriptObject[ToSnakeCase(prop.Name)] = value;
        }
        // use_auth tells UI templates whether the app signs users in; the app is found from the file being written.
        if (outputPath is not null && !scriptObject.ContainsKey("use_auth"))
            scriptObject["use_auth"] = AppAuth.Enabled(outputPath);
        // Every project file targets the app's framework; a model that names none gets the one of the app being written.
        if (!scriptObject.ContainsKey("target_framework"))
            scriptObject["target_framework"] = AppFramework.Resolve(outputPath ?? Directory.GetCurrentDirectory());
        // The package versions that follow the target, so a net8.0 app never references a net10.0 package.
        var target = scriptObject["target_framework"]!.ToString()!;
        scriptObject["ef_core_version"] = AppFramework.EfCoreVersion(target);
        scriptObject["coverlet_version"] = AppFramework.CoverletVersion(target);
        scriptObject["cryptography_version"] = AppFramework.CryptographyVersion(target);
        var context = new TemplateContext { TemplateLoader = new TemplateLoaderImpl(_assembly) };
        context.PushGlobal(scriptObject);
        return template.Render(context);
    }

    /// <summary>
    /// Renders a template and writes it to <paramref name="outputPath"/>,
    /// creating parent directories as needed. Routes through
    /// <see cref="Ux.WriteFile"/> so <c>--dry-run</c> and <c>--verbose</c>
    /// apply uniformly to every generated file.
    /// </summary>
    public void RenderToFile(
        string templatePath, object model, string outputPath)
    {
        var content = Render(templatePath, model, outputPath);
        Ux.WriteFile(outputPath, content);
    }

    /// <summary>
    /// Enables {{ include 'relative/path' }} in templates.
    /// </summary>
    private sealed class TemplateLoaderImpl(Assembly assembly) : ITemplateLoader
    {
        public string GetPath(TemplateContext context, SourceSpan callerSpan, string templatePath)
            => templatePath;

        public string Load(TemplateContext context, SourceSpan callerSpan, string templatePath)
        {
            var resourceName = $"Modulus.Cli.Templates.{templatePath.Replace('/', '.')}.sbn";
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException($"Template not found: {resourceName}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        public ValueTask<string?> LoadAsync(TemplateContext context, SourceSpan callerSpan, string templatePath)
            => ValueTask.FromResult<string?>(Load(context, callerSpan, templatePath));
    }

    /// <summary>
    /// Converts PascalCase property names to snake_case for template access.
    /// e.g. <c>RootNamespace</c> → <c>root_namespace</c>.
    /// </summary>
    private static string ToSnakeCase(string name) =>
        System.Text.RegularExpressions.Regex.Replace(
            name,
            "([a-z0-9])([A-Z])",
            "$1_$2").ToLowerInvariant();
}
