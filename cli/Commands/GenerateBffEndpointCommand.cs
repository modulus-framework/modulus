using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus generate-bff-endpoint Dashboard --bff mobile --modules Catalog,Orders</c>: scaffolds an aggregate endpoint
/// (<c>Endpoints/{Name}Endpoints.cs</c>) in a BFF that composes the read methods of the chosen modules' typed clients
/// concurrently, with per-section timeouts and degraded responses, and maps it in <c>Program.cs</c>. Missing module
/// clients are generated. An existing endpoint file is never overwritten.
/// </summary>
internal sealed class GenerateBffEndpointCommand : Command<GenerateBffEndpointCommand.Settings>
{
    private readonly TemplateEngine _templates = new();

    internal sealed class Settings : ModulusSettings
    {
        [Description("The endpoint's name in PascalCase, e.g. Dashboard (class DashboardEndpoints, route /dashboard).")]
        [CommandArgument(0, "<name>")]
        public string Name { get; init; } = "";

        [Description("The BFF clients to add it to (web, mobile, partner; comma-separated). Required when the app has more than one BFF.")]
        [CommandOption("--bff")]
        public string? Bff { get; init; }

        [Description("The modules whose data it composes (comma-separated). Default: every module with entities.")]
        [CommandOption("--modules")]
        public string? Modules { get; init; }

        [Description("The route (default: /{name in kebab-case}).")]
        [CommandOption("--route")]
        public string? Route { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => new GenerateBffEndpointCommand().ExecuteCore(s, Environment.CurrentDirectory));
    }

    internal int ExecuteCore(Settings s, string startDir)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(s.Name, "^[A-Z][A-Za-z0-9]*$"))
            throw new ArgumentException($"'{s.Name}' is not a PascalCase name (e.g. Dashboard).");

        var app = ModuleDiscovery.Inventory(startDir)
            ?? throw new InvalidOperationException("No .slnx file found in the current directory tree. Run this command from within a Modulus application.");
        if (app.Bffs.Count == 0)
            throw new InvalidOperationException("The app has no BFF. Add one first: modulus add-bff web|mobile|partner.");

        var targets = SelectBffs(app.Bffs, s.Bff);
        var modules = SelectModules(app.Modules, s.Modules);
        var route = NormalizeRoute(s.Route) ?? "/" + CodeGen.ToKebabCase(s.Name);

        foreach (var bff in targets)
        {
            var written = new List<string>();
            foreach (var module in modules)
                written.AddRange(BffApiClients.EnsureModule(_templates, bff, module.Name, module.Entities));

            var file = Path.Combine(bff.Directory, "Endpoints", $"{s.Name}Endpoints.cs");
            if (File.Exists(file))
            {
                AnsiConsole.MarkupLine("  [yellow]•[/] [grey]src/Bff/{0}/Endpoints/{1}Endpoints.cs[/] [yellow](exists, skipped)[/]", bff.ProjectName, s.Name);
            }
            else
            {
                _templates.RenderToFile("bff/AggregateEndpoint", Model(bff, s.Name, route, modules), file);
                written.Add($"Endpoints/{s.Name}Endpoints.cs");
            }

            if (BffApiClients.EnsureEndpointMapped(bff, $"Map{s.Name}Endpoints"))
                written.Add("Program.cs (updated)");

            AnsiConsole.MarkupLine("[green]✓[/] [cyan]GET {0}[/] on the [cyan]{1}[/] BFF", route, bff.Client);
            foreach (var f in written)
                AnsiConsole.MarkupLine("  [green]→[/] [grey]src/Bff/{0}/{1}[/]", bff.ProjectName, f);
        }

        return 0;
    }

    internal static IReadOnlyList<BffApiClients.BffProject> SelectBffs(IReadOnlyList<BffApiClients.BffProject> bffs, string? option)
    {
        if (string.IsNullOrWhiteSpace(option))
        {
            if (bffs.Count > 1)
                throw new ArgumentException($"The app has several BFFs ({string.Join(", ", bffs.Select(b => b.Client))}); pick them with --bff.");
            return bffs;
        }

        var wanted = BffClients.Parse(option);
        var missing = wanted.Where(c => bffs.All(b => b.Client != c)).ToList();
        if (missing.Count > 0)
            throw new ArgumentException($"The app has no {missing[0]} BFF. Add it with: modulus add-bff {missing[0]}.");
        return bffs.Where(b => wanted.Contains(b.Client)).ToList();
    }

    internal static IReadOnlyList<ModuleDiscovery.ModuleSummary> SelectModules(IReadOnlyList<ModuleDiscovery.ModuleSummary> all, string? option)
    {
        var withEntities = all.Where(m => m.Entities.Count > 0).ToList();
        if (string.IsNullOrWhiteSpace(option))
        {
            return withEntities.Count > 0
                ? withEntities
                : throw new ArgumentException("No module has entities yet; run modulus generate-crud first.");
        }

        var selected = new List<ModuleDiscovery.ModuleSummary>();
        foreach (var name in option.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var module = all.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Unknown module '{name}'. Modules: {string.Join(", ", all.Select(m => m.Name))}.");
            if (module.Entities.Count == 0)
                throw new ArgumentException($"Module '{module.Name}' has no entities yet; run modulus generate-crud first.");
            if (!selected.Contains(module))
                selected.Add(module);
        }

        return selected;
    }

    private static string? NormalizeRoute(string? route)
        => string.IsNullOrWhiteSpace(route) ? null : "/" + route.Trim().Trim('/');

    /// <summary>The template model: one optional section per entity list, named <c>{module}.{entities}</c>.</summary>
    internal static object Model(BffApiClients.BffProject bff, string name, string route, IReadOnlyList<ModuleDiscovery.ModuleSummary> modules)
    {
        var sections = modules
            .SelectMany(m => m.Entities.Select(e => (Module: m.Name, Plural: CodeGen.Pluralize(e))))
            .ToList();
        var duplicated = sections.GroupBy(x => x.Plural, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new
        {
            bff.ProjectName,
            ClientName = bff.Client,
            ClientPascal = char.ToUpperInvariant(bff.Client[0]) + bff.Client[1..],
            Name = name,
            Route = route,
            Modules = modules.Select(m => new { ModuleName = m.Name, Variable = CodeGen.ToCamelCase(m.Name) + "Api" }).ToList(),
            Sections = sections.Select(x => new
            {
                x.Plural,
                Key = $"{x.Module.ToLowerInvariant()}.{x.Plural.ToLowerInvariant()}",
                ClientVariable = CodeGen.ToCamelCase(x.Module) + "Api",
                Variable = duplicated.Contains(x.Plural) ? CodeGen.ToCamelCase(x.Module) + x.Plural : CodeGen.ToCamelCase(x.Plural),
            }).ToList(),
        };
    }
}
