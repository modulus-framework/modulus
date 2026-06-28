using System.ComponentModel;
using System.Text.RegularExpressions;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// Generates CRUD (Create, Read, Update, Delete) code for a domain entity
/// within an existing module.
/// </summary>
internal sealed class GenerateCrudCommand : Command<GenerateCrudCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [Description("Entity name (e.g. Product, Order)")]
        [CommandArgument(0, "<entity>")]
        public required string Entity { get; init; }

        [Description("Module name or namespace (e.g. Catalog, MyApp.Modules.Catalog)")]
        [CommandOption("-m|--module")]
        public string? Module { get; init; }

        [Description("Generate with additional fields (comma-separated: name:string,price:decimal)")]
        [CommandOption("--fields")]
        public string? Fields { get; init; }
    }

    private readonly TemplateEngine _templates = new();

    public override int Execute(CommandContext ctx, Settings s)
    {
        var entity = s.Entity;
        var entityLower = ToCamelCase(entity);
        var routeName = ToPluralLower(entity);

        // Resolve module directory
        var moduleDir = ResolveModuleDirectory(s.Module);
        var moduleNs = ResolveModuleNamespace(moduleDir!);
        var moduleName = moduleNs.Split('.').LastOrDefault() ?? "Module";
        var rootNs = ExtractRootNamespace(moduleNs);

        var model = new CrudModel
        {
            RootNamespace = rootNs,
            ModuleNamespace = moduleNs,
            ModuleName = moduleName,
            EntityName = entity,
            EntityNameLower = entityLower,
            RouteName = routeName,
        };

        var generated = new List<string>();

        // Domain layer
        var domainDir = Path.Combine(moduleDir!, "Domain");
        var entityFile = Path.Combine(domainDir, $"{entity}.cs");
        if (!File.Exists(entityFile))
        {
            _templates.RenderToFile("module/Entity", model, entityFile);
            generated.Add($"Domain/{entity}.cs");
        }

        var repoInterface = Path.Combine(domainDir, $"I{entity}Repository.cs");
        if (!File.Exists(repoInterface))
        {
            _templates.RenderToFile("module/IRepository", model, repoInterface);
            generated.Add($"Domain/I{entity}Repository.cs");
        }

        // Application layer
        var appDir = Path.Combine(moduleDir!, "Application");
        _templates.RenderToFile("module/Dto", model,
            Path.Combine(appDir, "Dtos", $"{entity}Dto.cs"));
        generated.Add($"Application/Dtos/{entity}Dto.cs");

        _templates.RenderToFile("module/CreateCommand", model,
            Path.Combine(appDir, $"Create{entity}Command.cs"));
        generated.Add($"Application/Create{entity}Command.cs");

        _templates.RenderToFile("module/CreateHandler", model,
            Path.Combine(appDir, $"Create{entity}Handler.cs"));
        generated.Add($"Application/Create{entity}Handler.cs");

        _templates.RenderToFile("module/GetAllQuery", model,
            Path.Combine(appDir, $"Get{ToPlural(entity)}Query.cs"));
        generated.Add($"Application/Get{ToPlural(entity)}Query.cs");

        _templates.RenderToFile("module/GetAllHandler", model,
            Path.Combine(appDir, $"Get{ToPlural(entity)}Handler.cs"));
        generated.Add($"Application/Get{ToPlural(entity)}Handler.cs");

        _templates.RenderToFile("module/GetByIdQuery", model,
            Path.Combine(appDir, $"Get{entity}ByIdQuery.cs"));
        generated.Add($"Application/Get{entity}ByIdQuery.cs");

        _templates.RenderToFile("module/GetByIdHandler", model,
            Path.Combine(appDir, $"Get{entity}ByIdHandler.cs"));
        generated.Add($"Application/Get{entity}ByIdHandler.cs");

        _templates.RenderToFile("module/UpdateCommand", model,
            Path.Combine(appDir, $"Update{entity}Command.cs"));
        generated.Add($"Application/Update{entity}Command.cs");

        _templates.RenderToFile("module/UpdateHandler", model,
            Path.Combine(appDir, $"Update{entity}Handler.cs"));
        generated.Add($"Application/Update{entity}Handler.cs");

        _templates.RenderToFile("module/DeleteCommand", model,
            Path.Combine(appDir, $"Delete{entity}Command.cs"));
        generated.Add($"Application/Delete{entity}Command.cs");

        _templates.RenderToFile("module/DeleteHandler", model,
            Path.Combine(appDir, $"Delete{entity}Handler.cs"));
        generated.Add($"Application/Delete{entity}Handler.cs");

        // Infrastructure layer
        var infraDir = Path.Combine(moduleDir!, "Infrastructure");
        _templates.RenderToFile("module/Repository", model,
            Path.Combine(infraDir, $"{entity}Repository.cs"));
        generated.Add($"Infrastructure/{entity}Repository.cs");

        // Summary
        AnsiConsole.MarkupLine("[green]✓[/] Generated CRUD for [cyan]{0}[/] in [grey]{1}[/]",
            entity, moduleName);
        foreach (var f in generated)
            AnsiConsole.MarkupLine("  [green]→[/] [grey]{0}[/]", f);

        AnsiConsole.MarkupLine("[grey]Remember to register I{0}Repository → {0}Repository in your module.[/]",
            entity);

        return 0;
    }

    private string? ResolveModuleDirectory(string? module)
    {
        var srcDir = Path.Combine(Environment.CurrentDirectory, "src");
        if (!Directory.Exists(srcDir))
            throw new InvalidOperationException(
                "No 'src' directory found. Run from the solution root.");

        // If module specified, find matching directory by suffix
        if (!string.IsNullOrEmpty(module))
        {
            var matches = Directory.GetDirectories(srcDir, $"*.{module}")
                .Where(d => d.Contains(".Modules."))
                .ToArray();
            if (matches.Length == 1) return matches[0];
            if (matches.Length == 0)
                throw new InvalidOperationException(
                    $"No module matching '{module}' found in src/.");
        }

        // Auto-detect: find .Modules.* directories
        var modDirs = Directory.GetDirectories(srcDir, "*.Modules.*");
        if (modDirs.Length == 1) return modDirs[0];
        if (modDirs.Length > 1)
            throw new InvalidOperationException(
                "Multiple modules found. Specify --module <name>.\n" +
                "Found: " + string.Join(", ", modDirs.Select(Path.GetFileName)));

        throw new InvalidOperationException(
            "Could not find a module directory. Run from the solution root " +
            "or specify --module <ModuleName>.");
    }

    /// <summary>
    /// Derives the module namespace from the resolved module directory name.
    /// Module directories are named after their namespace, e.g.
    /// <c>src/MyApp.Modules.Products</c> → <c>MyApp.Modules.Products</c>.
    /// </summary>
    private static string ResolveModuleNamespace(string moduleDir)
        => Path.GetFileName(moduleDir.TrimEnd(
               Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static string ExtractRootNamespace(string moduleNs)
    {
        var parts = moduleNs.Split('.');
        return parts.Length >= 2 ? string.Join(".", parts[..^2]) : moduleNs;
    }

    private static string ToCamelCase(string s) =>
        char.ToLowerInvariant(s[0]) + s[1..];

    private static string ToPlural(string s) =>
        s.EndsWith('s') ? s + "es" : s + "s";

    private static string ToPluralLower(string s) =>
        ToPlural(s).ToLowerInvariant();
}
