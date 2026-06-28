using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// Adds a module project to an existing application and wires it into
/// the host module's [DependsOn].
/// </summary>
internal sealed class AddModuleCommand : Command<AddModuleCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [Description("Name of the module to add (e.g. Orders, Billing)")]
        [CommandArgument(0, "<name>")]
        public required string Name { get; init; }
    }

    private readonly TemplateEngine _templates = new();

    public override int Execute(CommandContext ctx, Settings s)
    {
        var slnx = SolutionHelper.FindSolution(Environment.CurrentDirectory)
            ?? throw new InvalidOperationException(
                "No .slnx file found in the current directory tree. " +
                "Run this command from within a Modulus application.");

        var solutionDir = Path.GetDirectoryName(slnx)!;
        var rootNs = Path.GetFileNameWithoutExtension(slnx);
        var moduleName = s.Name;
        var moduleNs = $"{rootNs}.Modules.{moduleName}";
        var projectDir = Path.Combine(solutionDir, "src", moduleNs);

        if (Directory.Exists(projectDir))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Module already exists: {0}", projectDir);
            return 1;
        }

        var model = new ModuleModel
        {
            RootNamespace = rootNs,
            ModuleName = moduleName,
            ModuleNamespace = moduleNs,
            ModuleProject = $"{moduleNs}.csproj",
        };

        var appCmd = new NewAppCommand();
        appCmd.GenerateModule(projectDir, model, new AppModel
        {
            RootNamespace = rootNs,
            AppName = rootNs,
        });

        // Add project to .slnx
        var relPath = Path.GetRelativePath(solutionDir,
            Path.Combine(projectDir, model.ModuleProject));
        SolutionHelper.AddProject(slnx, relPath);

        // Wire [DependsOn] into the host module
        WireDependsOn(solutionDir, rootNs, moduleName);

        // Add ProjectReference from Host to module
        AddProjectReference(solutionDir, rootNs, moduleNs);

        AnsiConsole.MarkupLine("[green]✓[/] Module [cyan]{0}[/] added and wired into [grey]{1}[/]",
            moduleName, Path.GetFileName(slnx));
        AnsiConsole.MarkupLine("[grey]  →[/] Created [grey]{0}.csproj[/]", moduleNs);
        AnsiConsole.MarkupLine("[grey]  →[/] Added ProjectReference in Host");
        AnsiConsole.MarkupLine("[grey]  →[/] Added [[DependsOn]] to host module");

        return 0;
    }

    private static void WireDependsOn(string solutionDir, string rootNs, string moduleName)
    {
        // Find the host module file
        var hostDir = Path.Combine(solutionDir, "src", $"{rootNs}.Host");
        var moduleFiles = Directory.GetFiles(hostDir, "*Module.cs",
            SearchOption.AllDirectories);

        foreach (var file in moduleFiles)
        {
            var content = File.ReadAllText(file);
            var moduleClass = $"{rootNs}.Modules.{moduleName}Module";

            // Check if already wired
            if (content.Contains(moduleClass, StringComparison.OrdinalIgnoreCase))
                continue;

            // Add [DependsOn] attribute before the class declaration
            var classPattern = "public sealed class";
            var idx = content.IndexOf(classPattern);
            if (idx < 0) continue;

            var attribute = $"[DependsOn(typeof({rootNs}.Modules.{moduleName}Module))]\n";
            content = content.Insert(
                content.LastIndexOf('\n', idx) + 1,
                attribute);

            File.WriteAllText(file, content);
        }
    }

    private static void AddProjectReference(string solutionDir, string rootNs, string moduleNs)
    {
        var hostCsproj = Path.Combine(solutionDir, "src", $"{rootNs}.Host", $"{rootNs}.Host.csproj");
        var moduleCsproj = Path.Combine(solutionDir, "src", moduleNs, $"{moduleNs}.csproj");

        if (!File.Exists(hostCsproj)) return;

        var relRef = Path.GetRelativePath(
            Path.GetDirectoryName(hostCsproj)!,
            moduleCsproj).Replace('\\', '/');

        var content = File.ReadAllText(hostCsproj);
        if (content.Contains(relRef)) return;

        // Insert before </Project>
        var projectRef = $"  <ItemGroup>\n    <ProjectReference Include=\"{relRef}\" />\n  </ItemGroup>\n";
        var closeIdx = content.LastIndexOf("</Project>");
        if (closeIdx < 0) return;

        content = content.Insert(closeIdx, projectRef);
        File.WriteAllText(hostCsproj, content);
    }
}
