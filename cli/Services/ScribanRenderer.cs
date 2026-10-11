namespace Modulus.Cli.Services;

/// <summary>
/// Renders templates using simple string substitution.
/// Used for template rendering in Phase 3c.
/// </summary>
internal class ScribanRenderer
{
    /// <summary>Render a template by substituting variables</summary>
    public string Render(string templateContent, UiTemplateContext context)
    {
        try
        {
            var output = templateContent;

            // Simple string substitution for template variables
            output = output.Replace("{{ entity }}", context.Entity);
            output = output.Replace("{{ entityPlural }}", context.EntityPlural);
            output = output.Replace("{{ entityLower }}", context.EntityLower);
            output = output.Replace("{{ routeName }}", context.RouteName);
            output = output.Replace("{{ uiEngine }}", context.UiEngine);
            output = output.Replace("{{ namespace }}", context.Namespace);
            output = output.Replace("{{ now }}", context.Now.ToString("O"));

            // Module properties
            output = output.Replace("{{ module.rootNamespace }}", context.Module.RootNamespace);
            output = output.Replace("{{ module.moduleNamespace }}", context.Module.ModuleNamespace);
            output = output.Replace("{{ module.moduleName }}", context.Module.ModuleName);

            return output;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to render template: {ex.Message}", ex);
        }
    }
}
