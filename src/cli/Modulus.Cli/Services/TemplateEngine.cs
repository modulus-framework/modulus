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
    /// Renders a template by resource path (e.g. <c>"app/Program.cs"</c>).
    /// </summary>
    public string Render(string templatePath, object model)
    {
        var resourceName = $"Modulus.Cli.Templates.{templatePath.Replace('/', '.')}.sbn";
        using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException(
                $"Embedded template not found: {resourceName}");
        using var reader = new StreamReader(stream);
        var templateSource = reader.ReadToEnd();

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
        var context = new TemplateContext { TemplateLoader = new TemplateLoaderImpl(_assembly) };
        context.PushGlobal(scriptObject);
        return template.Render(context);
    }

    /// <summary>
    /// Renders a template and writes it to <paramref name="outputPath"/>,
    /// creating parent directories as needed.
    /// </summary>
    public void RenderToFile(
        string templatePath, object model, string outputPath)
    {
        var content = Render(templatePath, model);
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(outputPath, content);
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

        public ValueTask<string> LoadAsync(TemplateContext context, SourceSpan callerSpan, string templatePath)
            => ValueTask.FromResult(Load(context, callerSpan, templatePath));
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
