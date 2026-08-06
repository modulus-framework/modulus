using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// Generates a single query handler (not a full CRUD set) within an existing module.
/// </summary>
internal sealed class GenerateQueryCommand : Command<GenerateQueryCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Query name (e.g. GetOrderDetails, FetchUserProfile). Omit to be prompted.")]
        [CommandArgument(0, "[name]")]
        public string? Name { get; init; }

        [Description("Module name or namespace (e.g. Catalog, MyApp.Modules.Catalog). Auto-detected if one module.")]
        [CommandOption("-m|--module")]
        public string? Module { get; init; }
    }

    private readonly TemplateEngine _templates = new();

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => ExecuteCore(ctx, s));
    }

    private int ExecuteCore(CommandContext ctx, Settings s)
    {
        var featureName = !string.IsNullOrWhiteSpace(s.Name)
            ? CodeGen.ValidateIdentifier(s.Name, "Query")
            : Ux.AskRequired("Query name [grey](e.g. GetOrderDetails, FetchUserProfile)[/]:",
                ciHint: "Pass the query name, e.g. `modulus generate-query GetOrderDetails --module Orders`.");

        var featureNameLower = CodeGen.ToCamelCase(featureName);

        var module = CodeGen.ResolveModule(s.Module);
        var appDir = CodeGen.LayerDir(module.Directory, module.Namespace, "Application");
        var infraDir = CodeGen.LayerDir(module.Directory, module.Namespace, "Infrastructure");

        var model = new FeatureModel
        {
            RootNamespace = module.RootNamespace,
            ModuleNamespace = module.Namespace,
            ModuleName = module.Name,
            FeatureName = featureName,
            FeatureNameLower = featureNameLower,
        };

        var generated = new List<string>();
        var skipped = new List<string>();

        // ── Application layer (query + handler) ──────────────────
        var queryFile = Path.Combine(appDir, $"{featureName}Query.cs");
        var handlerFile = Path.Combine(appDir, $"{featureName}Handler.cs");

        if (!File.Exists(queryFile))
        {
            _templates.RenderToFile("module/Application/Query", model, queryFile);
            generated.Add(CodeGen.Rel(appDir, $"{featureName}Query.cs"));
        }
        else
        {
            skipped.Add(CodeGen.Rel(appDir, $"{featureName}Query.cs"));
        }

        if (!File.Exists(handlerFile))
        {
            _templates.RenderToFile("module/Application/QueryHandler", model, handlerFile);
            generated.Add(CodeGen.Rel(appDir, $"{featureName}Handler.cs"));
        }
        else
        {
            skipped.Add(CodeGen.Rel(appDir, $"{featureName}Handler.cs"));
        }

        // Wire handler registration into the module class.
        var moduleFile = Path.Combine(infraDir, $"{module.Name}Module.cs");
        if (File.Exists(moduleFile))
        {
            var wired = EnsureModuleRegistrations(moduleFile, module.Namespace, featureName);
            if (wired)
                generated.Add(CodeGen.Rel(infraDir, $"{module.Name}Module.cs (updated)"));
        }

        // ── Summary ───────────────────────────────────────────────
        AnsiConsole.MarkupLine("[green]✓[/] Generated query [cyan]{0}[/] in [grey]{1}[/]",
            featureName, module.Name);
        foreach (var f in generated)
            AnsiConsole.MarkupLine("  [green]→[/] [grey]{0}[/]", f);
        foreach (var f in skipped)
            AnsiConsole.MarkupLine("  [yellow]•[/] [grey]{0}[/] [yellow](exists, skipped)[/]", f);

        return 0;
    }

    /// <summary>
    /// Ensures mediator handlers are registered in the module class's ConfigureServices,
    /// idempotently. Uses a generic check to avoid duplicates if multiple handlers are added.
    /// </summary>
    private static bool EnsureModuleRegistrations(string moduleFile, string moduleNs, string featureName)
    {
        var content = File.ReadAllText(moduleFile);
        var original = content;

        var appNs = $"{moduleNs}.Application";

        // Ensure required usings.
        foreach (var u in new[] { "using Modulus.Mediator.Extensions;", $"using {appNs};" })
        {
            if (!content.Contains(u, StringComparison.Ordinal))
            {
                var nsIdx = content.IndexOf("namespace ", StringComparison.Ordinal);
                if (nsIdx >= 0)
                    content = content.Insert(nsIdx, u + "\n");
            }
        }

        // Insert mediator registration if not already present (generic check).
        // Reference the generated handler to ensure the assembly is loaded correctly.
        var handlerLine = $"        services.AddMediatorHandlers(typeof({featureName}Handler).Assembly);";
        if (!content.Contains("AddMediatorHandlers(typeof(", StringComparison.Ordinal))
            content = InsertInConfigureServices(content, handlerLine);

        if (content == original)
            return false;

        Ux.WriteFile(moduleFile, content);
        return true;
    }

    /// <summary>
    /// Inserts a line right after the opening brace of the ConfigureServices method body.
    /// </summary>
    private static string InsertInConfigureServices(string content, string line)
    {
        var methodIdx = content.IndexOf("ConfigureServices(", StringComparison.Ordinal);
        if (methodIdx < 0) return content;

        var bodyOpen = content.IndexOf('{', methodIdx);
        if (bodyOpen < 0) return content;

        return content.Insert(bodyOpen + 1, "\n" + line);
    }
}
